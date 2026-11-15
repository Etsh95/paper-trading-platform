using System.Collections.Concurrent;
using PaperTrading.Domain;

namespace PaperTrading.Application.MarketData;

public sealed record MarketStatus(string? Source, bool Connected, DateTimeOffset? LastQuoteAt);

/// <summary>
/// Read model: latest quote per symbol plus feed status, for the UI, the REST snapshot and the mock feed's seed. The engine keeps its own copy on its thread;
/// this one is for concurrent readers so they never queue behind order processing.
/// </summary>
public sealed class QuoteCache
{
    private readonly ConcurrentDictionary<string, Quote> _quotes = new(StringComparer.Ordinal);
    private volatile MarketStatus _status = new(null, false, null);

    public IReadOnlyCollection<Quote> All => _quotes.Values.OrderBy(q => q.Symbol, StringComparer.Ordinal).ToList();

    public MarketStatus Status => _status;

    public void Update(Quote quote, string source)
    {
        _quotes[quote.Symbol] = quote;
        _status = new MarketStatus(source, true, quote.Timestamp);
    }

    public void MarkDisconnected() => _status = _status with { Connected = false };
}
