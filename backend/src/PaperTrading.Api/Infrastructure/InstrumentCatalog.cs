using PaperTrading.Core.Domain;

namespace PaperTrading.Api.Infrastructure;

/// <param name="ReferencePrice">Starting price for the mock feed only.</param>
public sealed record CatalogEntry(Instrument Instrument, decimal ReferencePrice);

/// <summary>Tradable symbols. Filters mirror Binance spot rules for these pairs.</summary>
public static class InstrumentCatalog
{
    public static readonly IReadOnlyList<CatalogEntry> Default =
    [
        new(new Instrument("BTCUSDT", "BTC", "USDT", TickSize: 0.01m, StepSize: 0.00001m, MinNotional: 5m), 95_000m),
        new(new Instrument("ETHUSDT", "ETH", "USDT", TickSize: 0.01m, StepSize: 0.0001m, MinNotional: 5m), 3_400m),
        new(new Instrument("SOLUSDT", "SOL", "USDT", TickSize: 0.01m, StepSize: 0.001m, MinNotional: 5m), 180m),
        new(new Instrument("BNBUSDT", "BNB", "USDT", TickSize: 0.01m, StepSize: 0.001m, MinNotional: 5m), 650m),
        new(new Instrument("XRPUSDT", "XRP", "USDT", TickSize: 0.0001m, StepSize: 0.1m, MinNotional: 5m), 2.2m),
    ];
}
