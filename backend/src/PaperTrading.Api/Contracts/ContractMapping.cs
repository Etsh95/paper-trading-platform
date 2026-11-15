using PaperTrading.Application.MarketData;
using PaperTrading.Domain;
using PaperTrading.Domain.Matching;

namespace PaperTrading.Api.Contracts;

/// <summary>Domain snapshots to API contracts. Plain functions: explicit, and checked by the compiler.</summary>
public static class ContractMapping
{
    public static InstrumentResponse ToResponse(this Instrument i) =>
        new(i.Symbol, i.BaseAsset, i.QuoteAsset, i.TickSize, i.StepSize, i.MinNotional);

    public static TradingConfigResponse ToResponse(this TradingOptions o) =>
        new(o.InitialCash, o.FeeRate, (int)o.MaxQuoteAge.TotalSeconds, o.MaxOpenOrdersPerAccount);

    public static QuoteResponse ToResponse(this Quote q) => new(q.Symbol, q.Bid, q.Ask, q.Mid, q.Timestamp);

    public static MarketStatusResponse ToResponse(this MarketStatus s) => new(s.Source, s.Connected, s.LastQuoteAt);

    public static OrderResponse ToResponse(this OrderSnapshot o) => new(
        o.Id, o.Symbol, o.Side, o.Type, o.Quantity, o.LimitPrice, o.Status,
        o.FilledQuantity, o.FillPrice, o.Fee, o.RejectReason, o.CreatedAt, o.UpdatedAt);

    public static TradeResponse ToResponse(this Trade t) => new(
        t.Id, t.OrderId, t.Symbol, t.Side, t.Quantity, t.Price, t.Notional, t.Fee, t.RealizedPnl, t.ExecutedAt);

    public static PositionResponse ToResponse(this PositionValuation p) => new(
        p.Symbol, p.Quantity, p.ReservedQuantity, p.AverageCost, p.CostBasis, p.MarkPrice, p.MarketValue, p.UnrealizedPnl, p.RealizedPnl);

    public static PortfolioResponse ToResponse(this PortfolioValuation v) => new(
        v.InitialCash, v.Cash, v.ReservedCash, v.AvailableCash, v.PositionsValue, v.Equity,
        v.RealizedPnl, v.UnrealizedPnl, v.TotalPnl, v.TotalFees,
        v.Positions.Select(ToResponse).ToList(), v.AsOf);

    public static IReadOnlyList<TResponse> ToResponses<T, TResponse>(this IEnumerable<T> items, Func<T, TResponse> map) =>
        items.Select(map).ToList();
}
