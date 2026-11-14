using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using PaperTrading.Api.Infrastructure;
using PaperTrading.Core.Domain;

namespace PaperTrading.Api.MarketData;

/// <summary>
/// Mean-reverting random walk, snapped to the tick grid. Used for local development, demos, and as a fallback
/// when Binance is unreachable.
/// <para>
/// It starts from the <b>last real price</b> when one is known. Starting from a fixed reference instead would make
/// prices jump on failover and fill resting limit orders at levels the market never traded.
/// </para>
/// </summary>
public sealed class MockQuoteSource(IOptions<MarketDataOptions> options, TimeProvider time, QuoteCache cache) : IQuoteSource
{
    private const decimal MaxStep = 0.0006m;      // +-0.06% per tick
    private const decimal Reversion = 0.002m;     // pull back toward the reference price
    private const decimal HalfSpreadRatio = 0.00005m;

    public string Name => "Mock";

    public async IAsyncEnumerable<Quote> StreamAsync(IReadOnlyList<CatalogEntry> instruments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var random = new Random();
        var last = cache.All.ToDictionary(q => q.Symbol, q => q.Mid);
        var anchors = instruments.ToDictionary(
            e => e.Instrument.Symbol,
            e => last.TryGetValue(e.Instrument.Symbol, out var mid) ? ToTick(mid, e.Instrument.TickSize) : e.ReferencePrice);
        var mids = new Dictionary<string, decimal>(anchors);
        using var timer = new PeriodicTimer(options.Value.MockInterval, time);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            foreach (var (instrument, _) in instruments)
            {
                var reference = anchors[instrument.Symbol];
                var mid = mids[instrument.Symbol];
                // The random draw is a double because it is noise, not money; it becomes a decimal factor immediately.
                var shock = (decimal)(random.NextDouble() * 2 - 1) * MaxStep;
                var pull = (reference - mid) / reference * Reversion;
                mid = Math.Max(instrument.TickSize * 10, ToTick(mid * (1 + shock + pull), instrument.TickSize));
                mids[instrument.Symbol] = mid;

                var half = Math.Max(instrument.TickSize, ToTick(mid * HalfSpreadRatio, instrument.TickSize));
                yield return new Quote(instrument.Symbol, mid - half, mid + half, time.GetUtcNow());
            }
        }
    }

    private static decimal ToTick(decimal price, decimal tick) => Math.Round(price / tick, MidpointRounding.ToEven) * tick;
}
