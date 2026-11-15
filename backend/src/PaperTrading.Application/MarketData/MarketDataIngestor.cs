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
        _cache.Update(quote, source);
        return _sequencer.PublishQuoteAsync(quote, cancellationToken);
    }

    public void ReportDisconnected() => _cache.MarkDisconnected();
}
