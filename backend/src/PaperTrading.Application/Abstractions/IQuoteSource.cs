using PaperTrading.Domain;

namespace PaperTrading.Application.Abstractions;

/// <summary>Port for a market-data feed. The stream ending or throwing means "disconnected".</summary>
public interface IQuoteSource
{
    string Name { get; }

    IAsyncEnumerable<Quote> StreamAsync(IReadOnlyList<Instrument> instruments, CancellationToken cancellationToken);
}
