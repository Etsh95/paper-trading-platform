namespace PaperTrading.Domain;

/// <summary>Top of book for one symbol: best bid and best ask.</summary>
public sealed record Quote(string Symbol, decimal Bid, decimal Ask, DateTimeOffset Timestamp)
{
    /// <summary>Positive, not crossed, and bounded (so a corrupt feed value can't overflow order arithmetic).</summary>
    public bool IsValid => Bid > 0 && Ask >= Bid && Ask <= Money.MaxOrderMagnitude;

    public decimal Mid => (Bid + Ask) / 2;
}
