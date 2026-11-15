using Microsoft.Extensions.Options;
using PaperTrading.Api.Contracts;
using PaperTrading.Application.Abstractions;
using PaperTrading.Application.MarketData;
using PaperTrading.Domain.Matching;

namespace PaperTrading.Api.Endpoints;

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Market data");

        group.MapGet("/instruments", (IInstrumentCatalog catalog) =>
                TypedResults.Ok(catalog.Instruments.ToResponses(ContractMapping.ToResponse)))
            .WithSummary("Tradable symbols with tick size, step size and minimum order value.");

        group.MapGet("/config", (IOptions<TradingOptions> options) => TypedResults.Ok(options.Value.ToResponse()))
            .WithSummary("Trading rules the client needs for pre-trade estimates (fee rate, starting cash).");

        group.MapGet("/quotes", (QuoteCache cache) =>
                TypedResults.Ok(new QuotesResponse(cache.Status.ToResponse(), cache.All.ToResponses(ContractMapping.ToResponse))))
            .WithSummary("Latest best bid/ask per symbol and the feed status. Live updates are pushed on the SignalR hub.");

        return app;
    }
}
