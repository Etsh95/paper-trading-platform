using Microsoft.Extensions.Diagnostics.HealthChecks;
using PaperTrading.Core.Engine;

namespace PaperTrading.Api.MarketData;

/// <summary>Ready = receiving prices and the engine queue isn't backed up.</summary>
public sealed class MarketDataHealthCheck(QuoteCache cache, EngineSequencer engine, TimeProvider time) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = cache.Status;
        var data = new Dictionary<string, object>
        {
            ["source"] = status.Source ?? "none",
            ["engineQueue"] = engine.QueueLength,
        };

        if (status.LastQuoteAt is null || time.GetUtcNow() - status.LastQuoteAt > TimeSpan.FromSeconds(30))
            return Task.FromResult(HealthCheckResult.Degraded("No recent market data.", data: data));

        return Task.FromResult(engine.QueueLength > 5_000
            ? HealthCheckResult.Degraded("Engine queue is backing up.", data: data)
            : HealthCheckResult.Healthy("Receiving market data.", data));
    }
}
