using Microsoft.AspNetCore.SignalR;
using PaperTrading.Api.Contracts;
using PaperTrading.Application.MarketData;
using PaperTrading.Domain;

namespace PaperTrading.Api.Realtime;

/// <summary>
/// Pushes coalesced quotes to browsers at a fixed cadence. The engine sees every tick; browsers only need
/// the latest price a few times a second, so a fast feed (hundreds of updates/s) can't flood clients.
/// </summary>
public sealed class QuoteBroadcaster(
    QuoteCache cache,
    IHubContext<MarketHub> hub,
    RealtimeOptions options,
    TimeProvider time,
    ILogger<QuoteBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastSent = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        using var timer = new PeriodicTimer(options.BroadcastInterval, time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var changed = cache.All
                .Where(q => !lastSent.TryGetValue(q.Symbol, out var at) || q.Timestamp > at)
                .ToList();
            if (changed.Count == 0) continue;

            try
            {
                await hub.Clients.All.SendAsync("quotes", changed.ToResponses(ContractMapping.ToResponse), stoppingToken);
                // Record delivery only after a successful send, so a failed broadcast is retried on the next tick.
                foreach (var q in changed) lastSent[q.Symbol] = q.Timestamp;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Quote broadcast failed");
            }
        }
    }
}
