using PaperTrading.Domain;

namespace PaperTrading.Domain.Matching;

/// <summary>Outbound notifications produced by the engine, e.g. for pushing to clients.</summary>
public abstract record EngineEvent(Guid AccountId);

public sealed record OrderUpdated(Guid AccountId, OrderSnapshot Order) : EngineEvent(AccountId);

public sealed record TradeExecuted(Guid AccountId, Trade Trade) : EngineEvent(AccountId);
