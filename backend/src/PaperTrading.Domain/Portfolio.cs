namespace PaperTrading.Domain;

/// <summary>
/// Cash, reservations and positions for one account.
/// <para>
/// The methods here assume the caller (the matching engine) has already checked availability; they re-check and
/// throw because a violation would mean a bug, and failing loudly beats silently corrupting balances.
/// </para>
/// Accounting identity that always holds exactly: <c>Cash + Σ CostBasis == InitialCash + RealizedPnl</c>.
/// </summary>
public sealed class Portfolio
{
    private readonly Dictionary<string, Position> _positions = new(StringComparer.Ordinal);

    public Portfolio(decimal initialCash)
    {
        if (initialCash < 0) throw new ArgumentOutOfRangeException(nameof(initialCash));
        InitialCash = initialCash;
        Cash = initialCash;
    }

    public decimal InitialCash { get; }

    /// <summary>Total cash including amounts reserved by resting buy orders.</summary>
    public decimal Cash { get; private set; }

    public decimal ReservedCash { get; private set; }
    public decimal AvailableCash => Cash - ReservedCash;
    public decimal RealizedPnl { get; private set; }
    public decimal TotalFees { get; private set; }

    public IReadOnlyDictionary<string, Position> Positions => _positions;

    public decimal AvailableQuantity(string symbol) =>
        _positions.TryGetValue(symbol, out var p) ? p.AvailableQuantity : 0;

    public bool TryReserveCash(decimal amount)
    {
        RequirePositive(amount);
        if (AvailableCash < amount) return false;
        ReservedCash += amount;
        return true;
    }

    public void ReleaseCash(decimal amount)
    {
        RequireNonNegative(amount);
        if (amount > ReservedCash) throw new InvalidOperationException("Releasing more cash than reserved.");
        ReservedCash -= amount;
    }

    public bool TryReserveQuantity(string symbol, decimal quantity)
    {
        RequirePositive(quantity);
        if (!_positions.TryGetValue(symbol, out var p) || p.AvailableQuantity < quantity) return false;
        p.ReservedQuantity += quantity;
        return true;
    }

    public void ReleaseQuantity(string symbol, decimal quantity)
    {
        RequireNonNegative(quantity);
        if (quantity == 0) return;
        if (!_positions.TryGetValue(symbol, out var p) || quantity > p.ReservedQuantity)
            throw new InvalidOperationException("Releasing more quantity than reserved.");
        p.ReservedQuantity -= quantity;
    }

    /// <summary>Applies a buy fill. Any cash the order had reserved is released as part of the same step.</summary>
    public void ApplyBuy(string symbol, decimal quantity, decimal price, decimal fee, decimal releaseReservedCash = 0)
    {
        RequirePositive(quantity);
        RequirePositive(price);
        RequireNonNegative(fee);

        var cost = quantity * price + fee;
        ReleaseCash(releaseReservedCash);
        if (AvailableCash < cost)
        {
            ReservedCash += releaseReservedCash; // restore before failing
            throw new InvalidOperationException("Insufficient available cash for buy fill.");
        }

        Cash -= cost;
        TotalFees += fee;

        var position = GetOrAdd(symbol);
        position.Quantity += quantity;
        position.CostBasis += cost;
    }

    /// <summary>Applies a sell fill and returns the realized P&amp;L (net of the sell fee).</summary>
    public decimal ApplySell(string symbol, decimal quantity, decimal price, decimal fee, decimal releaseReservedQuantity = 0)
    {
        RequirePositive(quantity);
        RequirePositive(price);
        RequireNonNegative(fee);

        if (!_positions.TryGetValue(symbol, out var position))
            throw new InvalidOperationException($"No position in {symbol}.");

        ReleaseQuantity(symbol, releaseReservedQuantity);
        if (position.AvailableQuantity < quantity)
        {
            position.ReservedQuantity += releaseReservedQuantity;
            throw new InvalidOperationException("Insufficient available quantity for sell fill.");
        }

        // Pro-rata cost relief; a full close relieves the exact remaining cost basis (no residue).
        var costRelieved = quantity == position.Quantity
            ? position.CostBasis
            : Money.RoundQuote(position.CostBasis * quantity / position.Quantity);

        var proceeds = quantity * price - fee;
        var realized = proceeds - costRelieved;

        position.Quantity -= quantity;
        position.CostBasis -= costRelieved;
        position.RealizedPnl += realized;

        Cash += proceeds;
        TotalFees += fee;
        RealizedPnl += realized;
        return realized;
    }

    /// <summary>
    /// Marks positions to market. Long positions are valued at the <b>bid</b> (what they could be sold for now).
    /// If no quote is known, the position is carried at cost (unrealized P&amp;L of zero) and flagged.
    /// </summary>
    public PortfolioValuation Valuate(Func<string, Quote?> quoteFor, DateTimeOffset asOf)
    {
        var rows = new List<PositionValuation>();
        decimal marketValue = 0, unrealized = 0;

        foreach (var p in _positions.Values.Where(p => p.Quantity > 0).OrderBy(p => p.Symbol, StringComparer.Ordinal))
        {
            var mark = quoteFor(p.Symbol)?.Bid;
            var value = mark is { } m ? p.Quantity * m : p.CostBasis;
            var pnl = value - p.CostBasis;
            marketValue += value;
            unrealized += pnl;
            rows.Add(new PositionValuation(
                p.Symbol, p.Quantity, p.ReservedQuantity, p.AverageCost, p.CostBasis,
                mark, value, pnl, p.RealizedPnl));
        }

        return new PortfolioValuation(
            InitialCash, Cash, ReservedCash, AvailableCash, marketValue,
            Equity: Cash + marketValue,
            RealizedPnl, unrealized,
            TotalPnl: RealizedPnl + unrealized,
            TotalFees, rows, asOf);
    }

    private Position GetOrAdd(string symbol)
    {
        if (!_positions.TryGetValue(symbol, out var p))
        {
            p = new Position(symbol);
            _positions.Add(symbol, p);
        }
        return p;
    }

    private static void RequirePositive(decimal value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
    }

    private static void RequireNonNegative(decimal value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Must not be negative.");
    }
}

public sealed record PositionValuation(
    string Symbol,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AverageCost,
    decimal CostBasis,
    decimal? MarkPrice,
    decimal MarketValue,
    decimal UnrealizedPnl,
    decimal RealizedPnl);

public sealed record PortfolioValuation(
    decimal InitialCash,
    decimal Cash,
    decimal ReservedCash,
    decimal AvailableCash,
    decimal PositionsValue,
    decimal Equity,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal TotalPnl,
    decimal TotalFees,
    IReadOnlyList<PositionValuation> Positions,
    DateTimeOffset AsOf);
