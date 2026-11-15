using Microsoft.AspNetCore.Http.HttpResults;
using PaperTrading.Api.Contracts;
using PaperTrading.Api.Security;
using PaperTrading.Application.Trading;

namespace PaperTrading.Api.Endpoints;

public static class PortfolioEndpoints
{
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/portfolio", GetPortfolio)
            .RequireAuthorization()
            .WithTags("Portfolio")
            .WithSummary("Cash, reservations, open positions marked at the bid, realized and unrealized P&L.");
        return app;
    }

    private static async Task<Ok<PortfolioResponse>> GetPortfolio(HttpContext http, ITradingService trading, CancellationToken ct) =>
        TypedResults.Ok((await trading.GetPortfolioAsync(http.User.AccountId(), ct)).ToResponse());
}
