using System.Collections.Concurrent;
using PaperTrading.Core.Domain;

namespace PaperTrading.Api.MarketData;

public sealed record MarketStatus(string? Source, bool Connected, DateTimeOffset? LastQuoteAt);

/// <summary>
/// Latest quote per symbol for the UI and REST snapshot. The engine keeps its own copy on its thread;
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
