using PaperTrading.Domain;

namespace PaperTrading.Api.Contracts;

// Public API contract. Deliberately separate from domain types: the API can stay stable while the domain evolves,
// and internal details (account id, engine sequence, idempotency key) never leak to clients.
// Every decimal is serialized as a JSON string (see DecimalStringJsonConverter).

public sealed record SessionResponse(Guid AccountId, string AccessToken, DateTimeOffset ExpiresAt, decimal InitialCash);

public sealed record InstrumentResponse(string Symbol, string BaseAsset, string QuoteAsset, decimal TickSize, decimal StepSize, decimal MinNotional);

public sealed record TradingConfigResponse(decimal InitialCash, decimal FeeRate, int MaxQuoteAgeSeconds, int MaxOpenOrders);

public sealed record QuoteResponse(string Symbol, decimal Bid, decimal Ask, decimal Mid, DateTimeOffset Timestamp);

public sealed record MarketStatusResponse(string? Source, bool Connected, DateTimeOffset? LastQuoteAt);

public sealed record QuotesResponse(MarketStatusResponse Status, IReadOnlyList<QuoteResponse> Quotes);

public sealed record OrderResponse(
    Guid Id,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice,
    OrderStatus Status,
    decimal FilledQuantity,
    decimal? FillPrice,
    decimal Fee,
    string? RejectReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TradeResponse(
    Guid Id,
    Guid OrderId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    decimal Notional,
    decimal Fee,
    decimal RealizedPnl,
    DateTimeOffset ExecutedAt);

public sealed record PositionResponse(
    string Symbol,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AverageCost,
    decimal CostBasis,
    decimal? MarkPrice,
    decimal MarketValue,
    decimal UnrealizedPnl,
    decimal RealizedPnl);

public sealed record PortfolioResponse(
    decimal InitialCash,
    decimal Cash,
    decimal ReservedCash,
    decimal AvailableCash,
    decimal PositionsValue,
    decimal Equity,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal TotalPnl,
    decimal TotalFees,
    IReadOnlyList<PositionResponse> Positions,
    DateTimeOffset AsOf);
