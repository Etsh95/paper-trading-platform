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
    /// Upper bound for an order's quantity and price. Far beyond any real order, and it keeps every product the
    /// engine computes (quantity x price x fee rate, about 10^18) well inside decimal's range (about 7.9 x 10^28),
    /// so arithmetic can never overflow halfway through processing an order.
    /// </summary>
    public const decimal MaxOrderMagnitude = 1_000_000_000m;

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
