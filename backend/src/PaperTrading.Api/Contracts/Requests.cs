using PaperTrading.Domain;
using PaperTrading.Domain.Matching;

namespace PaperTrading.Api.Contracts;

/// <summary>Body of POST /api/orders. `required` members make missing fields a 400 at deserialization.</summary>
public sealed class PlaceOrderBody
{
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType Type { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? LimitPrice { get; init; }

    public PlaceOrderRequest ToDomain() => new(Symbol, Side, Type, Quantity, LimitPrice);
}
