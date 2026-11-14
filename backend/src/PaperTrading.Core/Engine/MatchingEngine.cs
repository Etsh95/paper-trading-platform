using PaperTrading.Core.Domain;

namespace PaperTrading.Core.Engine;

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
    public PortfolioValuation OpenAccount(Guid accountId) => GetPortfolio(accountId);

    public bool AccountExists(Guid accountId) => _accounts.ContainsKey(accountId);

    // ---------------------------------------------------------------- orders

    public PlaceOrderResult PlaceOrder(Guid accountId, PlaceOrderRequest request, string? idempotencyKey = null)
    {
        var account = GetOrCreateAccount(accountId);
        request = request.Normalize();

        // Idempotency check happens on the engine thread, so it is atomic with order creation:
        // concurrent duplicates are serialized and the second one always sees the first.
        if (idempotencyKey is not null && account.IdempotencyKeys.TryGetValue(idempotencyKey, out var previous))
        {
            return previous.Request == request
                ? PlaceOrderResult.From(previous.Order, replayed: true)
                : new PlaceOrderResult(PlaceOrderOutcome.IdempotencyConflict, previous.Order.ToSnapshot(),
                    ErrorCode: "idempotency_key_reused",
                    ErrorMessage: "This Idempotency-Key was already used with a different order request.");
        }

        if (Validate(request) is { } invalid) return invalid;

        var now = _time.GetUtcNow();
        var order = new Order(Guid.NewGuid(), accountId, ++_sequence, request.Symbol, request.Side, request.Type,
            request.Quantity, request.LimitPrice, idempotencyKey, now);

        account.Orders.Add(order.Id, order);
        account.OrderHistory.Add(order);
        if (idempotencyKey is not null) account.IdempotencyKeys.Add(idempotencyKey, (request, order));

        if (order.Type == OrderType.Market) ExecuteMarket(account, order);
        else PlaceLimit(account, order);

        if (order.Status != OrderStatus.Filled) Emit(new OrderUpdated(accountId, order.ToSnapshot()));
        return PlaceOrderResult.From(order, replayed: false);
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

    private PlaceOrderResult? Validate(PlaceOrderRequest r)
    {
        if (!_instruments.TryGetValue(r.Symbol, out var instrument))
            return PlaceOrderResult.Invalid(RejectReasons.UnknownSymbol, $"Unknown symbol '{r.Symbol}'.");

        if (!instrument.IsValidQuantity(r.Quantity))
            return PlaceOrderResult.Invalid(RejectReasons.InvalidQuantity,
                $"Quantity must be positive and a multiple of {instrument.StepSize}.");

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
