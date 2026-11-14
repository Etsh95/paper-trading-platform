namespace PaperTrading.Core.Domain;

/// <summary>
/// A tradable pair with exchange-style filters (modelled on Binance spot symbol rules).
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
    public bool IsValidPrice(decimal price) => price > 0 && price % TickSize == 0;

    public bool IsValidQuantity(decimal quantity) => quantity > 0 && quantity % StepSize == 0;
}
