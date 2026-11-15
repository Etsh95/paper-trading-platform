using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaperTrading.Application.Abstractions;
using PaperTrading.Application.MarketData;
using PaperTrading.Application.Trading;
using PaperTrading.Domain.Matching;

namespace PaperTrading.Application;

public static class DependencyInjection
{
    public const string TradingSection = "Trading";

    /// <summary>
    /// Registers the engine and use cases. Requires an <see cref="IInstrumentCatalog"/> (infrastructure) and
    /// optionally an <see cref="IEngineEventSink"/> (presentation).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TradingOptions>(configuration.GetSection(TradingSection));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(sp => new MatchingEngine(
            sp.GetRequiredService<IInstrumentCatalog>().Instruments,
            sp.GetRequiredService<IOptions<TradingOptions>>().Value,
            sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton(sp => new EngineSequencer(
            sp.GetRequiredService<MatchingEngine>(),
            sp.GetService<IEngineEventSink>(),
            sp.GetRequiredService<ILogger<EngineSequencer>>()));

        services.AddSingleton<ITradingService>(sp => new TradingService(sp.GetRequiredService<EngineSequencer>()));
        services.AddSingleton<QuoteCache>();
        services.AddSingleton(sp => new MarketDataIngestor(sp.GetRequiredService<EngineSequencer>(), sp.GetRequiredService<QuoteCache>()));
        return services;
    }
}
