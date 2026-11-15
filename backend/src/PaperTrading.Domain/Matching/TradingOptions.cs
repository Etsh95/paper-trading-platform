namespace PaperTrading.Domain.Matching;

/// <summary>Engine rules. Bound from configuration by the application layer; the domain only consumes values.</summary>
public sealed class TradingOptions
{
    /// <summary>Starting virtual balance in the quote asset (USDT).</summary>
    public decimal InitialCash { get; set; } = 100_000m;

    /// <summary>Fee charged on every fill, as a fraction of notional (0.001 = 0.1%, Binance's base spot rate).</summary>
    public decimal FeeRate { get; set; } = 0.001m;

    /// <summary>Market orders are refused if the latest quote is older than this.</summary>
    public TimeSpan MaxQuoteAge { get; set; } = TimeSpan.FromSeconds(10);

    public int MaxOpenOrdersPerAccount { get; set; } = 100;

    // Memory bounds. State lives in one process, so every per-account collection is capped.

    /// <summary>New accounts are refused beyond this many (anonymous sign-up is otherwise unbounded).</summary>
    public int MaxAccounts { get; set; } = 20_000;

    /// <summary>Finished orders kept per account; the oldest finished ones are dropped first. Open orders are never dropped.</summary>
    public int MaxOrderHistoryPerAccount { get; set; } = 500;

    /// <summary>Executions kept per account (oldest dropped first).</summary>
    public int MaxTradesPerAccount { get; set; } = 500;

    /// <summary>How long an idempotency key is remembered. After this, the same key is treated as a new order.</summary>
    public TimeSpan IdempotencyKeyLifetime { get; set; } = TimeSpan.FromHours(24);
}
