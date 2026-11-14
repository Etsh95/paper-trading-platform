using PaperTrading.Api.Infrastructure;
using PaperTrading.Api.MarketData;
using Microsoft.Extensions.Options;
using PaperTrading.Core.Domain;
using PaperTrading.Core.Engine;

namespace PaperTrading.Api.Endpoints;

public sealed record QuotesResponse(MarketStatus Status, IReadOnlyCollection<Quote> Quotes);

public sealed record TradingConfigResponse(decimal InitialCash, decimal FeeRate, int MaxQuoteAgeSeconds, int MaxOpenOrders);

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Market data");

        group.MapGet("/instruments", () => InstrumentCatalog.Default.Select(e => e.Instrument))
            .WithSummary("Tradable symbols and their tick size, step size and minimum notional.");

        group.MapGet("/config", (IOptions<TradingOptions> options) =>
            {
                var o = options.Value;
                return new TradingConfigResponse(o.InitialCash, o.FeeRate, (int)o.MaxQuoteAge.TotalSeconds, o.MaxOpenOrdersPerAccount);
            })
            .WithSummary("Trading rules the client needs for pre-trade estimates (fee rate, starting cash).");

        group.MapGet("/quotes", (QuoteCache cache) => new QuotesResponse(cache.Status, cache.All))
            .WithSummary("Latest best bid/ask per symbol. Live updates are pushed on the SignalR hub.");

        return app;
    }
}
