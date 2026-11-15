using PaperTrading.Application.Trading;
using PaperTrading.Domain;

namespace PaperTrading.Application.MarketData;

/// <summary>
/// The single entry point for prices coming into the system, whatever feed they come from. Every valid tick
/// goes to the engine (so resting limit orders can't miss a touch) and to the read cache.
/// </summary>
public sealed class MarketDataIngestor
{
    private readonly EngineSequencer _sequencer;
    private readonly QuoteCache _cache;

    internal MarketDataIngestor(EngineSequencer sequencer, QuoteCache cache)
    {
        _sequencer = sequencer;
        _cache = cache;
    }

    public ValueTask PublishAsync(Quote quote, string source, CancellationToken cancellationToken)
    {
        if (!quote.IsValid) return ValueTask.CompletedTask;
        return PublishValidAsync(quote, source, cancellationToken);
    }

    // The engine gets the tick first; the read cache is updated only once the engine has accepted it, so the cache
    // can never show a price the engine never received (e.g. if queueing is cancelled at shutdown).
    private async ValueTask PublishValidAsync(Quote quote, string source, CancellationToken cancellationToken)
    {
        await _sequencer.PublishQuoteAsync(quote, cancellationToken);
        _cache.Update(quote, source);
    }

    public void ReportDisconnected() => _cache.MarkDisconnected();
}
