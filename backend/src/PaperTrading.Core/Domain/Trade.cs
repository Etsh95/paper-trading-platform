namespace PaperTrading.Core.Domain;

/// <summary>An execution (fill). Immutable once created.</summary>
/// <param name="RealizedPnl">P&amp;L realized by this fill (sells only; buys realize nothing, their fee goes into cost basis).</param>
public sealed record Trade(
    Guid Id,
    Guid OrderId,
    Guid AccountId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    decimal Notional,
    decimal Fee,
    decimal RealizedPnl,
    DateTimeOffset ExecutedAt);
