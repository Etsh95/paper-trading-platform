using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using PaperTrading.Application.Abstractions;
using PaperTrading.Application.MarketData;
using PaperTrading.Domain;
using PaperTrading.Infrastructure.Instruments;

namespace PaperTrading.Infrastructure.MarketData;

/// <summary>
/// Mean-reverting random walk, snapped to the tick grid. Used for local development, demos, and as a fallback
/// when Binance is unreachable.
/// <para>
/// It starts from the <b>last real price</b> when one is known. Starting from a fixed reference instead would make
/// prices jump on failover and fill resting limit orders at levels the market never traded.
/// </para>
/// </summary>
internal sealed class MockQuoteSource(IOptions<MarketDataOptions> options, TimeProvider time, QuoteCache cache, StaticInstrumentCatalog catalog) : IQuoteSource
{
    private const decimal MaxStep = 0.0006m;      // +-0.06% per tick
    private const decimal Reversion = 0.002m;     // pull back toward the reference price
    private const decimal HalfSpreadRatio = 0.00005m;

    public string Name => "Mock";

    public async IAsyncEnumerable<Quote> StreamAsync(IReadOnlyList<Instrument> instruments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var random = new Random();
        // Continue from the last real quote: its mid as the anchor, and at least its spread. Starting tighter than the
        // live market would let failover fill resting limits placed inside the real spread.
        var last = cache.All.ToDictionary(q => q.Symbol);
        var anchors = instruments.ToDictionary(
            i => i.Symbol,
            i => last.TryGetValue(i.Symbol, out var q) ? ToTick(q.Mid, i.TickSize) : catalog.ReferencePrice(i.Symbol));
        var minHalfSpreads = instruments.ToDictionary(
            i => i.Symbol,
            i => last.TryGetValue(i.Symbol, out var q) ? CeilToTick((q.Ask - q.Bid) / 2, i.TickSize) : 0m);
        var mids = new Dictionary<string, decimal>(anchors);
        using var timer = new PeriodicTimer(options.Value.MockInterval, time);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            foreach (var instrument in instruments)
            {
                var reference = anchors[instrument.Symbol];
                var mid = mids[instrument.Symbol];
                // The random draw is a double because it is noise, not money; it becomes a decimal factor immediately.
                var shock = (decimal)(random.NextDouble() * 2 - 1) * MaxStep;
                var pull = (reference - mid) / reference * Reversion;
                mid = Math.Max(instrument.TickSize * 10, ToTick(mid * (1 + shock + pull), instrument.TickSize));
                mids[instrument.Symbol] = mid;

                var half = Math.Max(Math.Max(instrument.TickSize, ToTick(mid * HalfSpreadRatio, instrument.TickSize)), minHalfSpreads[instrument.Symbol]);
                yield return new Quote(instrument.Symbol, mid - half, mid + half, time.GetUtcNow());
            }
        }
    }

    private static decimal ToTick(decimal price, decimal tick) => Math.Round(price / tick, MidpointRounding.ToEven) * tick;

    private static decimal CeilToTick(decimal value, decimal tick) => Math.Ceiling(value / tick) * tick;
}
