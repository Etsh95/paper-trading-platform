using PaperTrading.Domain;
using PaperTrading.Domain.Matching;

namespace PaperTrading.Application.Trading;

/// <summary>
/// Trading use cases. The presentation layer depends on this, never on the engine or its sequencer, so how
/// commands are serialized (today: one in-process queue; at scale: a shard per account group) can change freely.
/// All results are immutable snapshots.
/// </summary>
public interface ITradingService
{
    /// <summary>
    /// Whether the account exists. State is in memory, so after a restart a still-valid token can point at an
    /// account that no longer exists; authentication uses this to reject such tokens instead of silently
    /// handing the user a fresh, empty account.
    /// </summary>
    Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>Creates the paper account if needed and returns its opening valuation; null when at account capacity.</summary>
    Task<PortfolioValuation?> OpenAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Places an order exactly once per idempotency key. Cancellation only stops the request from being queued:
    /// once queued it executes, and a retry with the same key returns the outcome.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(Guid accountId, PlaceOrderRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<CancelOrderResult> CancelOrderAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken = default);

    Task<OrderSnapshot?> GetOrderAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSnapshot>> GetOrdersAsync(Guid accountId, OrderStatus? status, int limit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Trade>> GetTradesAsync(Guid accountId, int limit, CancellationToken cancellationToken = default);

    Task<PortfolioValuation> GetPortfolioAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>The quote the engine executes against right now (it can trail the market-data cache by the queue).</summary>
    Task<Quote?> GetExecutionQuoteAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Operations waiting for the engine; a health and back-pressure signal.</summary>
    int PendingOperations { get; }
}
