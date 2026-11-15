using PaperTrading.Domain;
using PaperTrading.Domain.Matching;

namespace PaperTrading.Application.Trading;

/// <summary>Runs each use case as one operation on the engine's single-writer queue.</summary>
internal sealed class TradingService(EngineSequencer sequencer) : ITradingService
{
    public Task<PortfolioValuation?> OpenAccountAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        sequencer.ExecuteAsync(e => e.OpenAccount(accountId), cancellationToken);

    public Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        sequencer.ExecuteAsync(e => e.AccountExists(accountId), cancellationToken);

    public Task<PlaceOrderResult> PlaceOrderAsync(Guid accountId, PlaceOrderRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        return sequencer.ExecuteAsync(e => e.PlaceOrder(accountId, request, idempotencyKey), cancellationToken);
    }

    public Task<CancelOrderResult> CancelOrderAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken = default) =>
        sequencer.ExecuteAsync(e => e.CancelOrder(accountId, orderId), cancellationToken);

    public Task<OrderSnapshot?> GetOrderAsync(Guid accountId, Guid orderId, CancellationToken cancellationToken = default) =>
        sequencer.ExecuteAsync(e => e.GetOrder(accountId, orderId), cancellationToken);

    public Task<IReadOnlyList<OrderSnapshot>> GetOrdersAsync(Guid accountId, OrderStatus? status, int limit, CancellationToken cancellationToken = default) =>
        sequencer.ExecuteAsync(e => e.GetOrders(accountId, status, limit), cancellationToken);

    public Task<IReadOnlyList<Trade>> GetTradesAsync(Guid accountId, int limit, CancellationToken cancellationToken = default) =>
        sequencer.ExecuteAsync(e => e.GetTrades(accountId, limit), cancellationToken);

    public Task<PortfolioValuation> GetPortfolioAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        sequencer.ExecuteAsync(e => e.GetPortfolio(accountId), cancellationToken);

    public Task<Quote?> GetExecutionQuoteAsync(string symbol, CancellationToken cancellationToken = default) =>
        sequencer.ExecuteAsync(e => e.GetQuote(symbol), cancellationToken);

    public int PendingOperations => sequencer.QueueLength;
}
