namespace PaperTrading.Core.Domain;

/// <summary>
/// Long-only spot position. Cost basis is tracked as a <b>total</b> (including buy fees) rather than as an
/// average price, so closing the whole position relieves exactly the recorded cost and leaves no rounding residue.
/// </summary>
public sealed class Position
{
    internal Position(string symbol) => Symbol = symbol;

    public string Symbol { get; }
    public decimal Quantity { get; internal set; }
    public decimal ReservedQuantity { get; internal set; }
    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    /// <summary>Total cost (notional + buy fees) of the quantity currently held.</summary>
    public decimal CostBasis { get; internal set; }

    /// <summary>Cumulative P&amp;L realized on this symbol (net of sell fees).</summary>
    public decimal RealizedPnl { get; internal set; }

    /// <summary>Derived, display-only. Never used for accounting.</summary>
    public decimal AverageCost => Quantity == 0 ? 0 : Money.RoundQuote(CostBasis / Quantity);
}
