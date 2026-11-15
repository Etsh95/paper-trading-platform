using PaperTrading.Domain;

namespace PaperTrading.Domain.Matching;

/// <summary>
/// Paper-trading matching simulator. Executes orders against the live top of book.
/// <para>
/// <b>Not thread-safe by design.</b> It is driven by exactly one thread (see <see cref="EngineSequencer"/>),
/// which gives a total order over orders and price ticks and keeps this class free of locks, deterministic
/// and straightforward to unit test.
/// </para>
/// Execution rules:
/// <list type="bullet">
/// <item>Market buy fills at the ask, market sell at the bid. Refused without a fresh quote.</item>
/// <item>A limit order that is marketable on arrival fills immediately at the touch (price improvement).</item>
/// <item>Otherwise it rests, with its funds/quantity reserved, and fills at its limit price once the touch crosses it.</item>
/// <item>Full fills only: the simulator assumes the top of book has unlimited size.</item>
/// </list>
/// </summary>
public sealed class MatchingEngine
{
    private readonly Dictionary<string, Instrument> _instruments;
    private readonly Dictionary<string, RestingOrderBook> _books;
    private readonly Dictionary<string, Quote> _quotes = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Account> _accounts = [];
    private readonly List<EngineEvent> _outbox = [];
    private readonly TradingOptions _options;
    private readonly TimeProvider _time;
    private long _sequence;

    public MatchingEngine(IEnumerable<Instrument> instruments, TradingOptions options, TimeProvider? time = null)
    {
        _instruments = instruments.ToDictionary(i => i.Symbol, StringComparer.Ordinal);
        _books = _instruments.Keys.ToDictionary(s => s, _ => new RestingOrderBook(), StringComparer.Ordinal);
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyCollection<Instrument> Instruments => _instruments.Values;

    // ---------------------------------------------------------------- accounts

    /// <summary>Opens the account if it does not exist yet. Idempotent.</summary>
    /// <summary>
    /// Opens the account if it does not exist yet (idempotent). Returns null when the platform is at its account
    /// capacity (<see cref="TradingOptions.MaxAccounts"/>), so anonymous sign-ups can't grow memory without bound.
    /// </summary>
    public PortfolioValuation? OpenAccount(Guid accountId)
    {
        if (!_accounts.ContainsKey(accountId) && _accounts.Count >= _options.MaxAccounts) return null;
        return GetPortfolio(accountId);
    }

    public bool AccountExists(Guid accountId) => _accounts.ContainsKey(accountId);

    // ---------------------------------------------------------------- orders

    public PlaceOrderResult PlaceOrder(Guid accountId, PlaceOrderRequest request, string? idempotencyKey = null)
    {
        var account = GetOrCreateAccount(accountId);
        request = request.Normalize();

        // Idempotency check happens on the engine thread, so it is atomic with order creation:
        // concurrent duplicates are serialized and the second one always sees the first.
        var now = _time.GetUtcNow();
        (PlaceOrderRequest Request, Order Order, DateTimeOffset At) previous = default;
        var known = idempotencyKey is not null && account.IdempotencyKeys.TryGetValue(idempotencyKey, out previous);
        if (known && now - previous.At > _options.IdempotencyKeyLifetime)
        {
            account.IdempotencyKeys.Remove(idempotencyKey!); // expired: the key starts a new order
            known = false;
        }

        if (known)
        {
            var prior = previous.Order!; // set whenever the key is known
            return previous.Request == request
                ? PlaceOrderResult.From(prior, replayed: true)
                : new PlaceOrderResult(PlaceOrderOutcome.IdempotencyConflict, prior.ToSnapshot(),
                    ErrorCode: "idempotency_key_reused",
                    ErrorMessage: "This Idempotency-Key was already used with a different order request.");
        }

        if (Validate(request) is { } invalid) return invalid;

        var order = new Order(Guid.NewGuid(), accountId, ++_sequence, request.Symbol, request.Side, request.Type,
            request.Quantity, request.LimitPrice, idempotencyKey, now);

        account.Orders.Add(order.Id, order);
        account.OrderHistory.Add(order);
        if (idempotencyKey is not null) account.IdempotencyKeys[idempotencyKey] = (request, order, now);

        if (order.Type == OrderType.Market) ExecuteMarket(account, order);
        else PlaceLimit(account, order);

        if (order.Status != OrderStatus.Filled) Emit(new OrderUpdated(accountId, order.ToSnapshot()));
        var result = PlaceOrderResult.From(order, replayed: false);
        PruneHistory(account, now);
        return result;
    }

    public CancelOrderResult CancelOrder(Guid accountId, Guid orderId)
    {
        if (!_accounts.TryGetValue(accountId, out var account) || !account.Orders.TryGetValue(orderId, out var order))
            return new CancelOrderResult(CancelOrderOutcome.NotFound);

        if (order.Status != OrderStatus.Open)
            return new CancelOrderResult(CancelOrderOutcome.NotCancellable, order.ToSnapshot());

        _books[order.Symbol].Remove(order);
        ReleaseReservation(account, order);
        account.OpenOrderCount--;
        order.MarkCancelled(_time.GetUtcNow());

        var snapshot = order.ToSnapshot();
        Emit(new OrderUpdated(accountId, snapshot));
        return new CancelOrderResult(CancelOrderOutcome.Cancelled, snapshot);
    }

    // ---------------------------------------------------------------- market data

    /// <summary>
    /// Applies a price tick and fills every resting order the new touch crosses.
    /// Invalid quotes (crossed, non-positive) and unknown symbols are ignored.
    /// </summary>
    public void ApplyQuote(Quote quote)
    {
        if (!quote.IsValid || !_books.TryGetValue(quote.Symbol, out var book)) return;
        _quotes[quote.Symbol] = quote;

        foreach (var order in book.TakeTriggered(quote))
        {
            var account = _accounts[order.AccountId];
            var price = order.LimitPrice!.Value;
            account.OpenOrderCount--;
            // Funds/quantity were reserved at placement, so this fill cannot fail for lack of balance.
            Fill(account, order, price, Money.Fee(order.Quantity * price, _options.FeeRate));
        }
    }

    public Quote? GetQuote(string symbol) => _quotes.GetValueOrDefault(symbol);

    // ---------------------------------------------------------------- queries (return immutable snapshots)

    public PortfolioValuation GetPortfolio(Guid accountId) =>
        GetOrCreateAccount(accountId).Portfolio.Valuate(GetQuote, _time.GetUtcNow());

    public IReadOnlyList<OrderSnapshot> GetOrders(Guid accountId, OrderStatus? status = null, int limit = 200) =>
        _accounts.TryGetValue(accountId, out var account)
            ? account.OrderHistory
                .AsEnumerable().Reverse()
                .Where(o => status is null || o.Status == status)
                .Take(limit)
                .Select(o => o.ToSnapshot())
                .ToList()
            : [];

    public OrderSnapshot? GetOrder(Guid accountId, Guid orderId) =>
        _accounts.TryGetValue(accountId, out var account) && account.Orders.TryGetValue(orderId, out var order)
            ? order.ToSnapshot()
            : null;

    public IReadOnlyList<Trade> GetTrades(Guid accountId, int limit = 200) =>
        _accounts.TryGetValue(accountId, out var account)
            ? account.Trades.AsEnumerable().Reverse().Take(limit).ToList()
            : [];

    /// <summary>Returns and clears the events produced since the last call.</summary>
    public IReadOnlyList<EngineEvent> DrainEvents()
    {
        if (_outbox.Count == 0) return [];
        var events = _outbox.ToArray();
        _outbox.Clear();
        return events;
    }

    // ---------------------------------------------------------------- internals

    /// <summary>
    /// Validates everything that doesn't depend on market state. Runs before anything is recorded, so every later step
    /// works on well-formed, bounded input and cannot fail halfway through an order.
    /// </summary>
    private PlaceOrderResult? Validate(PlaceOrderRequest r)
    {
        // The API rejects these too, but the domain must not rely on its callers.
        if (!Enum.IsDefined(r.Side) || !Enum.IsDefined(r.Type))
            return PlaceOrderResult.Invalid(RejectReasons.InvalidOrderType, "Unknown order side or type.");

        if (!_instruments.TryGetValue(r.Symbol, out var instrument))
            return PlaceOrderResult.Invalid(RejectReasons.UnknownSymbol, "Unknown symbol.");

        if (!instrument.IsValidQuantity(r.Quantity) || r.Quantity > Money.MaxOrderMagnitude)
            return PlaceOrderResult.Invalid(RejectReasons.InvalidQuantity,
                $"Quantity must be positive, at most {Money.MaxOrderMagnitude}, and a multiple of {instrument.StepSize}.");

        if (r.LimitPrice > Money.MaxOrderMagnitude)
            return PlaceOrderResult.Invalid(RejectReasons.InvalidPrice, $"Limit price must be at most {Money.MaxOrderMagnitude}.");

        switch (r.Type)
        {
            case OrderType.Market when r.LimitPrice is not null:
                return PlaceOrderResult.Invalid(RejectReasons.LimitPriceNotAllowed, "Market orders must not specify a limit price.");

            case OrderType.Limit when r.LimitPrice is null:
                return PlaceOrderResult.Invalid(RejectReasons.LimitPriceRequired, "Limit orders require a limit price.");

            case OrderType.Limit when !instrument.IsValidPrice(r.LimitPrice!.Value):
                return PlaceOrderResult.Invalid(RejectReasons.InvalidPrice,
                    $"Limit price must be positive and a multiple of {instrument.TickSize}.");

            case OrderType.Limit when r.Quantity * r.LimitPrice!.Value < instrument.MinNotional:
                return PlaceOrderResult.Invalid(RejectReasons.BelowMinNotional,
                    $"Order value must be at least {instrument.MinNotional} {instrument.QuoteAsset}.");
        }

        return null;
    }

    private void ExecuteMarket(Account account, Order order)
    {
        if (TryGetFreshQuote(order.Symbol, out var quote, out var reason))
            ExecuteAtTouch(account, order, quote);
        else
            Reject(order, reason);
    }

    private void PlaceLimit(Account account, Order order)
    {
        var limit = order.LimitPrice!.Value;

        // Marketable on arrival: take the touch, which is at least as good as the limit.
        if (TryGetFreshQuote(order.Symbol, out var quote, out _) &&
            (order.Side == OrderSide.Buy ? quote.Ask <= limit : quote.Bid >= limit))
        {
            ExecuteAtTouch(account, order, quote);
            return;
        }

        if (account.OpenOrderCount >= _options.MaxOpenOrdersPerAccount)
        {
            Reject(order, RejectReasons.TooManyOpenOrders);
            return;
        }

        var portfolio = account.Portfolio;
        if (order.Side == OrderSide.Buy)
        {
            var notional = order.Quantity * limit;
            var reserve = notional + Money.Fee(notional, _options.FeeRate);
            if (!portfolio.TryReserveCash(reserve))
            {
                Reject(order, RejectReasons.InsufficientFunds);
                return;
            }
            order.ReservedCash = reserve;
        }
        else
        {
            if (!portfolio.TryReserveQuantity(order.Symbol, order.Quantity))
            {
                Reject(order, RejectReasons.InsufficientPosition);
                return;
            }
            order.ReservedQuantity = order.Quantity;
        }

        _books[order.Symbol].Add(order);
        account.OpenOrderCount++;
    }

    private void ExecuteAtTouch(Account account, Order order, Quote quote)
    {
        var instrument = _instruments[order.Symbol];
        var price = order.Side == OrderSide.Buy ? quote.Ask : quote.Bid;
        var notional = order.Quantity * price;
        var fee = Money.Fee(notional, _options.FeeRate);

        if (notional < instrument.MinNotional)
            Reject(order, RejectReasons.BelowMinNotional);
        else if (order.Side == OrderSide.Buy && account.Portfolio.AvailableCash < notional + fee)
            Reject(order, RejectReasons.InsufficientFunds);
        else if (order.Side == OrderSide.Sell && account.Portfolio.AvailableQuantity(order.Symbol) < order.Quantity)
            Reject(order, RejectReasons.InsufficientPosition);
        else
            Fill(account, order, price, fee);
    }

    private void Fill(Account account, Order order, decimal price, decimal fee)
    {
        decimal realized = 0;
        if (order.Side == OrderSide.Buy)
            account.Portfolio.ApplyBuy(order.Symbol, order.Quantity, price, fee, releaseReservedCash: order.ReservedCash);
        else
            realized = account.Portfolio.ApplySell(order.Symbol, order.Quantity, price, fee, releaseReservedQuantity: order.ReservedQuantity);

        order.ReservedCash = 0;
        order.ReservedQuantity = 0;

        var now = _time.GetUtcNow();
        order.MarkFilled(price, fee, now);

        var trade = new Trade(Guid.NewGuid(), order.Id, account.Id, order.Symbol, order.Side,
            order.Quantity, price, order.Quantity * price, fee, realized, now);
        account.Trades.Add(trade);
        if (account.Trades.Count > _options.MaxTradesPerAccount) account.Trades.RemoveAt(0);

        Emit(new TradeExecuted(account.Id, trade));
        Emit(new OrderUpdated(account.Id, order.ToSnapshot()));
    }

    private void ReleaseReservation(Account account, Order order)
    {
        if (order.ReservedCash > 0) account.Portfolio.ReleaseCash(order.ReservedCash);
        if (order.ReservedQuantity > 0) account.Portfolio.ReleaseQuantity(order.Symbol, order.ReservedQuantity);
        order.ReservedCash = 0;
        order.ReservedQuantity = 0;
    }

    private void Reject(Order order, string reason) => order.MarkRejected(reason, _time.GetUtcNow());

    private bool TryGetFreshQuote(string symbol, out Quote quote, out string reason)
    {
        if (!_quotes.TryGetValue(symbol, out quote!))
        {
            reason = RejectReasons.NoMarketData;
            return false;
        }

        if (_time.GetUtcNow() - quote.Timestamp > _options.MaxQuoteAge)
        {
            reason = RejectReasons.StaleMarketData;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Keeps per-account memory bounded: drops the oldest finished orders beyond the history cap (open orders hold
    /// reservations and are never dropped) and forgets idempotency keys past their lifetime or whose order was dropped.
    /// </summary>
    private void PruneHistory(Account account, DateTimeOffset now)
    {
        var excess = account.OrderHistory.Count - _options.MaxOrderHistoryPerAccount;
        for (var i = 0; excess > 0 && i < account.OrderHistory.Count;)
        {
            var old = account.OrderHistory[i];
            if (old.Status == OrderStatus.Open) { i++; continue; }
            account.OrderHistory.RemoveAt(i);
            account.Orders.Remove(old.Id);
            // Only forget the key if it still belongs to this order: an expired key may have been reused since.
            if (old.IdempotencyKey is not null && account.IdempotencyKeys.TryGetValue(old.IdempotencyKey, out var entry)
                && ReferenceEquals(entry.Order, old))
                account.IdempotencyKeys.Remove(old.IdempotencyKey);
            excess--;
        }

        if (account.IdempotencyKeys.Count > _options.MaxOrderHistoryPerAccount)
        {
            foreach (var key in account.IdempotencyKeys.Where(k => now - k.Value.At > _options.IdempotencyKeyLifetime).Select(k => k.Key).ToList())
                account.IdempotencyKeys.Remove(key);
        }
    }

    private Account GetOrCreateAccount(Guid accountId)
    {
        if (!_accounts.TryGetValue(accountId, out var account))
        {
            account = new Account(accountId, _options.InitialCash, _time.GetUtcNow());
            _accounts.Add(accountId, account);
        }
        return account;
    }

    private void Emit(EngineEvent e) => _outbox.Add(e);

    /// <summary>Test hook for invariant checks.</summary>
    internal Portfolio? FindPortfolio(Guid accountId) => _accounts.GetValueOrDefault(accountId)?.Portfolio;

    /// <summary>Test hook: open orders still holding a reservation.</summary>
    internal IEnumerable<Order> OpenOrders(Guid accountId) =>
        _accounts.TryGetValue(accountId, out var a) ? a.Orders.Values.Where(o => o.Status == OrderStatus.Open) : [];
}
