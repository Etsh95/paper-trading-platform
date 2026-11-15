namespace PaperTrading.Domain;

/// <summary>
/// A tradable pair with exchange-style filters (modelled on Binance spot symbol rules).
/// Invalid filters are rejected at construction: a zero increment would otherwise surface much later as a
/// divide-by-zero while validating an order.
/// </summary>
/// <param name="TickSize">Minimum price increment. Limit prices must be a multiple of it.</param>
/// <param name="StepSize">Minimum quantity increment. Quantities must be a multiple of it.</param>
/// <param name="MinNotional">Minimum order value (quantity x price) in the quote asset.</param>
public sealed record Instrument(
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    decimal TickSize,
    decimal StepSize,
    decimal MinNotional)
{
    public string Symbol { get; } = !string.IsNullOrWhiteSpace(Symbol)
        ? Symbol
        : throw new ArgumentException("Symbol is required.", nameof(Symbol));

    public decimal TickSize { get; } = TickSize > 0
        ? TickSize
        : throw new ArgumentOutOfRangeException(nameof(TickSize), TickSize, "Tick size must be positive.");

    public decimal StepSize { get; } = StepSize > 0
        ? StepSize
        : throw new ArgumentOutOfRangeException(nameof(StepSize), StepSize, "Step size must be positive.");

    public decimal MinNotional { get; } = MinNotional >= 0
        ? MinNotional
        : throw new ArgumentOutOfRangeException(nameof(MinNotional), MinNotional, "Minimum notional must not be negative.");

    public bool IsValidPrice(decimal price) => price > 0 && price % TickSize == 0;

    public bool IsValidQuantity(decimal quantity) => quantity > 0 && quantity % StepSize == 0;
}
