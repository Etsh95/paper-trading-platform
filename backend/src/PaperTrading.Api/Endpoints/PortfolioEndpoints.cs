using PaperTrading.Api.Infrastructure;
using PaperTrading.Core.Engine;

namespace PaperTrading.Api.Endpoints;

public static class PortfolioEndpoints
{
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/portfolio", async (HttpContext http, EngineSequencer engine, CancellationToken ct) =>
            {
                var accountId = http.User.AccountId();
                return await engine.ExecuteAsync(e => e.GetPortfolio(accountId), ct);
            })
            .RequireAuthorization()
            .WithTags("Portfolio")
            .WithSummary("Cash, reservations, open positions marked at bid, realized and unrealized P&L.");

        return app;
    }
}
