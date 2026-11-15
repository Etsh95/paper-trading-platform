using Microsoft.Extensions.Diagnostics.HealthChecks;
using PaperTrading.Application.MarketData;
using PaperTrading.Application.Trading;

namespace PaperTrading.Api.Health;

/// <summary>Ready = receiving prices and the engine queue isn't backed up.</summary>
public sealed class MarketDataHealthCheck(QuoteCache cache, ITradingService trading, TimeProvider time) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = cache.Status;
        var data = new Dictionary<string, object>
        {
            ["source"] = status.Source ?? "none",
            ["engineQueue"] = trading.PendingOperations,
        };

        if (status.LastQuoteAt is null || time.GetUtcNow() - status.LastQuoteAt > TimeSpan.FromSeconds(30))
            return Task.FromResult(HealthCheckResult.Degraded("No recent market data.", data: data));

        return Task.FromResult(trading.PendingOperations > 5_000
            ? HealthCheckResult.Degraded("Engine queue is backing up.", data: data)
            : HealthCheckResult.Healthy("Receiving market data.", data));
    }
}
