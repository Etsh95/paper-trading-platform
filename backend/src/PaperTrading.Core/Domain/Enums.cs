namespace PaperTrading.Core.Domain;

public enum OrderSide
{
    Buy,
    Sell,
}

public enum OrderType
{
    Market,
    Limit,
}

public enum OrderStatus
{
    /// <summary>Resting limit order waiting for the market to reach its price.</summary>
    Open,
    Filled,
    Cancelled,
    /// <summary>Accepted for processing but refused for a business reason (funds, stale price...).</summary>
    Rejected,
}
