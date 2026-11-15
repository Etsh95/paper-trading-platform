using PaperTrading.Domain.Matching;

namespace PaperTrading.Application.Abstractions;

/// <summary>
/// Port for outbound engine events (fills, order updates), e.g. pushed to clients by the presentation layer.
/// Called on the engine thread, so implementations must only enqueue and never block.
/// </summary>
public interface IEngineEventSink
{
    void Publish(IReadOnlyList<EngineEvent> events);
}
