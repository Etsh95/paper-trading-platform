using PaperTrading.Api.Auth;
using PaperTrading.Api.Infrastructure;
using PaperTrading.Core.Engine;

namespace PaperTrading.Api.Endpoints;

public sealed record SessionResponse(Guid AccountId, string AccessToken, DateTimeOffset ExpiresAt, decimal InitialCash);

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sessions", async (TokenService tokens, EngineSequencer engine, CancellationToken ct) =>
            {
                var accountId = Guid.NewGuid();
                var portfolio = await engine.ExecuteAsync(e => e.OpenAccount(accountId), ct);
                var (token, expiresAt) = tokens.Issue(accountId);
                return TypedResults.Created("/api/portfolio", new SessionResponse(accountId, token, expiresAt, portfolio.InitialCash));
            })
            .RequireRateLimiting(RateLimitPolicies.Sessions)
            .WithTags("Session")
            .WithSummary("Create an anonymous paper-trading account and get a bearer token.");

        return app;
    }
}
