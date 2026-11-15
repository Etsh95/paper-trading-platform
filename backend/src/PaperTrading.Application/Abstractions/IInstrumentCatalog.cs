using PaperTrading.Domain;

namespace PaperTrading.Application.Abstractions;

/// <summary>Port for the set of tradable instruments and their exchange rules.</summary>
public interface IInstrumentCatalog
{
    IReadOnlyList<Instrument> Instruments { get; }
}
