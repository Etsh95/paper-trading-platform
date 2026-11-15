using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaperTrading.Application.Abstractions;
using PaperTrading.Infrastructure.Instruments;
using PaperTrading.Infrastructure.MarketData;

namespace PaperTrading.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers adapters: the instrument catalog, the Binance and mock feeds, and the feed service.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MarketDataOptions>(configuration.GetSection(MarketDataOptions.SectionName));

        services.AddSingleton<StaticInstrumentCatalog>();
        services.AddSingleton<IInstrumentCatalog>(sp => sp.GetRequiredService<StaticInstrumentCatalog>());

        services.AddSingleton<IQuoteSource, BinanceQuoteSource>();
        services.AddSingleton<IQuoteSource, MockQuoteSource>();
        services.AddHostedService<MarketDataService>();
        return services;
    }
}
