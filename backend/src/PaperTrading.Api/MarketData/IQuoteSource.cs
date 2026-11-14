using PaperTrading.Api.Infrastructure;
using PaperTrading.Core.Domain;

namespace PaperTrading.Api.MarketData;

/// <summary>A stream of top-of-book quotes. The stream ending or throwing means "disconnected".</summary>
public interface IQuoteSource
{
    string Name { get; }

    IAsyncEnumerable<Quote> StreamAsync(IReadOnlyList<CatalogEntry> instruments, CancellationToken cancellationToken);
}
