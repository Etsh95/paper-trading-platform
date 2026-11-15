using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaperTrading.Application.Abstractions;
using PaperTrading.Application.MarketData;

namespace PaperTrading.Infrastructure.MarketData;

/// <summary>
/// Feed service: pulls quotes from the configured source and hands them to the application's
/// <see cref="MarketDataIngestor"/>, which feeds the engine and the read cache.
/// Owns connection policy: exponential backoff, fail over to the mock feed, and periodic retry of the primary.
/// </summary>
public sealed class MarketDataService(
    IEnumerable<IQuoteSource> sources,
    IOptions<MarketDataOptions> options,
    MarketDataIngestor ingestor,
    IInstrumentCatalog catalog,
    TimeProvider time,
    ILogger<MarketDataService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var all = sources.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        if (!all.TryGetValue(settings.Provider, out var primary))
            throw new InvalidOperationException($"Unknown market data provider '{settings.Provider}'. Known: {string.Join(", ", all.Keys)}");

        var current = primary;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            // On the fallback feed, run it for a while and then give the primary another chance.
            var onFallback = current != primary;
            using var run = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            if (onFallback) run.CancelAfter(settings.RetryPrimaryAfter);

            try
            {
                await foreach (var quote in current.StreamAsync(catalog.Instruments, run.Token))
                {
                    failures = 0;
                    await ingestor.PublishAsync(quote, current.Name, stoppingToken);
                }
                logger.LogWarning("{Source} stream ended", current.Name);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (onFallback && run.IsCancellationRequested)
            {
                logger.LogInformation("Retrying primary feed {Source}", primary.Name);
                current = primary;
                failures = 0;
                continue;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Source} stream failed", current.Name);
            }

            ingestor.ReportDisconnected();
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
