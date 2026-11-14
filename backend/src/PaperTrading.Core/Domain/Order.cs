namespace PaperTrading.Core.Domain;

/// <summary>
/// Mutable order entity. Owned exclusively by the matching engine (single writer), so it needs no locking.
/// Anything leaving the engine is an immutable <see cref="OrderSnapshot"/>.
/// </summary>
public sealed class Order
{
    internal Order(
        Guid id,
        Guid accountId,
        long sequence,
        string symbol,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? limitPrice,
        string? idempotencyKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        Sequence = sequence;
        Symbol = symbol;
        Side = side;
        Type = type;
        Quantity = quantity;
        LimitPrice = limitPrice;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Status = OrderStatus.Open;
    }

    public Guid Id { get; }
    public Guid AccountId { get; }

    /// <summary>Engine-wide arrival sequence. Used for time priority and deterministic ordering.</summary>
    public long Sequence { get; }

    public string Symbol { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }
    public decimal Quantity { get; }
    public decimal? LimitPrice { get; }
    public string? IdempotencyKey { get; }
    public DateTimeOffset CreatedAt { get; }

    public OrderStatus Status { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public decimal FilledQuantity { get; private set; }
    public decimal? FillPrice { get; private set; }
    public decimal Fee { get; private set; }
    public string? RejectReason { get; private set; }

    /// <summary>Cash held back for a resting buy (notional at limit + fee).</summary>
    internal decimal ReservedCash { get; set; }

    /// <summary>Base quantity held back for a resting sell.</summary>
    internal decimal ReservedQuantity { get; set; }

    internal void MarkFilled(decimal price, decimal fee, DateTimeOffset at)
    {
        EnsureOpen();
        Status = OrderStatus.Filled;
        FilledQuantity = Quantity;
        FillPrice = price;
        Fee = fee;
        UpdatedAt = at;
    }

    internal void MarkCancelled(DateTimeOffset at)
    {
        EnsureOpen();
        Status = OrderStatus.Cancelled;
        UpdatedAt = at;
    }

    internal void MarkRejected(string reason, DateTimeOffset at)
    {
        EnsureOpen();
        Status = OrderStatus.Rejected;
        RejectReason = reason;
        UpdatedAt = at;
    }

    private void EnsureOpen()
    {
        if (Status != OrderStatus.Open)
            throw new InvalidOperationException($"Order {Id} is {Status}; state transition not allowed.");
    }

    public OrderSnapshot ToSnapshot() => new(
        Id, AccountId, Sequence, Symbol, Side, Type, Quantity, LimitPrice, Status,
        FilledQuantity, FillPrice, Fee, RejectReason, IdempotencyKey, CreatedAt, UpdatedAt);
}

/// <summary>Immutable read model of an order, safe to hand to other threads and serialize.</summary>
public sealed record OrderSnapshot(
    Guid Id,
    Guid AccountId,
    long Sequence,
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
    string? IdempotencyKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
