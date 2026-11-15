using Microsoft.AspNetCore.Http.HttpResults;
using PaperTrading.Api.Auth;
using PaperTrading.Api.Contracts;
using PaperTrading.Api.Security;
using PaperTrading.Application.Trading;

namespace PaperTrading.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sessions", CreateSession)
            .RequireRateLimiting(RateLimitPolicies.Sessions)
            .WithTags("Session")
            .WithSummary("Create an anonymous paper-trading account and get a bearer token.");
        return app;
    }

    private static async Task<Results<Created<SessionResponse>, ProblemHttpResult>> CreateSession(ITradingService trading, TokenService tokens, CancellationToken ct)
    {
        var accountId = Guid.NewGuid();
        var opening = await trading.OpenAccountAsync(accountId, ct);
        if (opening is null)
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The platform is at capacity. Try again later.",
                extensions: new Dictionary<string, object?> { ["code"] = "capacity_reached" });
        var (token, expiresAt) = tokens.Issue(accountId);
        return TypedResults.Created("/api/portfolio", new SessionResponse(accountId, token, expiresAt, opening.InitialCash));
    }
}
