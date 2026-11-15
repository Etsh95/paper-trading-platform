using PaperTrading.Domain;

namespace PaperTrading.Domain.Matching;

/// <summary>
/// The order as requested by the client. A record, so value equality doubles as the idempotency
/// fingerprint: the same key with a different request is a conflict.
/// </summary>
public sealed record PlaceOrderRequest(
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice = null)
{
    internal PlaceOrderRequest Normalize() => this with { Symbol = Symbol.Trim().ToUpperInvariant() };
}

public enum PlaceOrderOutcome
{
    /// <summary>Order was filled immediately or is resting on the book.</summary>
    Accepted,
    /// <summary>Order was recorded but refused for a business reason; see <see cref="OrderSnapshot.RejectReason"/>.</summary>
    Rejected,
    /// <summary>Request was malformed; nothing was recorded and the idempotency key was not consumed.</summary>
    Invalid,
    /// <summary>The idempotency key was already used with a different request.</summary>
    IdempotencyConflict,
}

public sealed record PlaceOrderResult(
    PlaceOrderOutcome Outcome,
    OrderSnapshot? Order = null,
    bool Replayed = false,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    internal static PlaceOrderResult Invalid(string code, string message) =>
        new(PlaceOrderOutcome.Invalid, ErrorCode: code, ErrorMessage: message);

    internal static PlaceOrderResult From(Order order, bool replayed) => new(
        order.Status == OrderStatus.Rejected ? PlaceOrderOutcome.Rejected : PlaceOrderOutcome.Accepted,
        order.ToSnapshot(),
        replayed,
        order.RejectReason);
}

public enum CancelOrderOutcome
{
    Cancelled,
    NotFound,
    NotCancellable,
}

public sealed record CancelOrderResult(CancelOrderOutcome Outcome, OrderSnapshot? Order = null);
