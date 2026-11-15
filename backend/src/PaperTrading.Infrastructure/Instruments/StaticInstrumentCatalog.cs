using PaperTrading.Application.Abstractions;
using PaperTrading.Domain;

namespace PaperTrading.Infrastructure.Instruments;

/// <summary>Tradable symbols. Filters mirror Binance spot rules for these pairs.</summary>
public sealed class StaticInstrumentCatalog : IInstrumentCatalog
{
    private static readonly (Instrument Instrument, decimal ReferencePrice)[] Entries =
    [
        (new Instrument("BTCUSDT", "BTC", "USDT", TickSize: 0.01m, StepSize: 0.00001m, MinNotional: 5m), 95_000m),
        (new Instrument("ETHUSDT", "ETH", "USDT", TickSize: 0.01m, StepSize: 0.0001m, MinNotional: 5m), 3_400m),
        (new Instrument("SOLUSDT", "SOL", "USDT", TickSize: 0.01m, StepSize: 0.001m, MinNotional: 5m), 180m),
        (new Instrument("BNBUSDT", "BNB", "USDT", TickSize: 0.01m, StepSize: 0.001m, MinNotional: 5m), 650m),
        (new Instrument("XRPUSDT", "XRP", "USDT", TickSize: 0.0001m, StepSize: 0.1m, MinNotional: 5m), 2.2m),
    ];

    public IReadOnlyList<Instrument> Instruments { get; } = Entries.Select(e => e.Instrument).ToArray();

    /// <summary>Starting price for the simulated feed when no real price has been seen yet.</summary>
    public decimal ReferencePrice(string symbol) =>
        Entries.First(e => e.Instrument.Symbol == symbol).ReferencePrice;
}
