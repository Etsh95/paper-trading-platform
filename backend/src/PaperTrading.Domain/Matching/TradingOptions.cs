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
}
