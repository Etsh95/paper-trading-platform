using Microsoft.Extensions.Options;
using PaperTrading.Api.Infrastructure;
using PaperTrading.Core.Engine;

namespace PaperTrading.Api.MarketData;

/// <summary>
/// Feed service: pulls quotes from the configured source and broadcasts them to the rest of the system
/// (the matching engine via the sequencer, and the UI via <see cref="QuoteCache"/>).
/// Reconnects with exponential backoff and can fail over to the mock feed.
/// </summary>
public sealed class MarketDataService(
    IEnumerable<IQuoteSource> sources,
    IOptions<MarketDataOptions> options,
    EngineSequencer sequencer,
    QuoteCache cache,
    TimeProvider time,
    ILogger<MarketDataService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var all = sources.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        if (!all.TryGetValue(settings.Provider, out var current))
            throw new InvalidOperationException($"Unknown market data provider '{settings.Provider}'. Known: {string.Join(", ", all.Keys)}");

        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var quote in current.StreamAsync(InstrumentCatalog.Default, stoppingToken))
                {
                    failures = 0;
                    cache.Update(quote, current.Name);
                    await sequencer.PublishQuoteAsync(quote, stoppingToken);
                }
                logger.LogWarning("{Source} stream ended", current.Name);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Source} stream failed", current.Name);
            }

            cache.MarkDisconnected();
            failures++;

            if (settings.FallbackToMock && failures >= settings.FailuresBeforeFallback &&
                current.Name != "Mock" && all.TryGetValue("Mock", out var mock))
            {
                logger.LogError("{Source} failed {Failures} times in a row; falling back to the mock feed", current.Name, failures);
                current = mock;
                failures = 0;
                continue;
            }

            var backoff = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, failures)));
            try
            {
                await Task.Delay(backoff, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
