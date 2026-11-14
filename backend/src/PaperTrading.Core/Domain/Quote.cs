using System.Text.Json.Serialization;

namespace PaperTrading.Core.Domain;

/// <summary>Top of book for one symbol: best bid and best ask.</summary>
public sealed record Quote(string Symbol, decimal Bid, decimal Ask, DateTimeOffset Timestamp)
{
    [JsonIgnore]
    public bool IsValid => Bid > 0 && Ask >= Bid;

    public decimal Mid => (Bid + Ask) / 2;
}
