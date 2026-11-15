namespace PaperTrading.Domain;

/// <summary>
/// Central rounding policy for quote-currency (USDT) amounts.
/// All monetary values are <see cref="decimal"/>; rounding happens only here, in one place.
/// </summary>
public static class Money
{
    /// <summary>Quote currency precision (USDT on Binance uses 8 decimal places).</summary>
    public const int QuoteScale = 8;

    /// <summary>
    /// Fee for a notional amount. Rounded <b>up</b> to <see cref="QuoteScale"/> so rounding never
    /// works in the trader's favour (the platform never under-charges).
    /// </summary>
    public static decimal Fee(decimal notional, decimal feeRate)
    {
        if (notional < 0) throw new ArgumentOutOfRangeException(nameof(notional));
        if (feeRate < 0) throw new ArgumentOutOfRangeException(nameof(feeRate));
        return Math.Round(notional * feeRate, QuoteScale, MidpointRounding.ToPositiveInfinity);
    }

    /// <summary>Banker's rounding to quote precision, used for pro-rata cost allocations.</summary>
    public static decimal RoundQuote(decimal amount) =>
        Math.Round(amount, QuoteScale, MidpointRounding.ToEven);
}
