using PaperTrading.Api.Infrastructure;
using PaperTrading.Api.MarketData;
using PaperTrading.Core.Domain;

namespace PaperTrading.Api.Endpoints;

public sealed record QuotesResponse(MarketStatus Status, IReadOnlyCollection<Quote> Quotes);

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Market data");

        group.MapGet("/instruments", () => InstrumentCatalog.Default.Select(e => e.Instrument))
            .WithSummary("Tradable symbols and their tick size, step size and minimum notional.");

        group.MapGet("/quotes", (QuoteCache cache) => new QuotesResponse(cache.Status, cache.All))
            .WithSummary("Latest best bid/ask per symbol. Live updates are pushed on the SignalR hub.");

        return app;
    }
}
