using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace PaperTrading.Api.Security;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int SessionsPerMinutePerIp { get; set; } = 10;
    public int OrdersPerSecondPerAccount { get; set; } = 10;
    public int OrderBurstPerAccount { get; set; } = 20;
}

public static class RateLimitPolicies
{
    public const string Sessions = "sessions";
    public const string Orders = "orders";

    public static IServiceCollection AddPaperTradingRateLimits(this IServiceCollection services)
    {
        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            static RateLimitOptions Limits(HttpContext ctx) =>
                ctx.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

            o.AddPolicy(Sessions, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Limits(ctx).SessionsPerMinutePerIp, Window = TimeSpan.FromMinutes(1) }));

            o.AddPolicy(Orders, ctx => RateLimitPartition.GetTokenBucketLimiter(
                ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = Limits(ctx).OrderBurstPerAccount,
                    TokensPerPeriod = Limits(ctx).OrdersPerSecondPerAccount,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                }));
        });
    }
}
