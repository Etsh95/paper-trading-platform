namespace PaperTrading.Domain;

/// <summary>
/// Stable machine-readable codes. Clients localize these (the UI ships English and Arabic),
/// so they are part of the API contract and must not change casually.
/// </summary>
public static class RejectReasons
{
    // Business rejections: the order is recorded with status Rejected.
    public const string NoMarketData = "no_market_data";
    public const string StaleMarketData = "stale_market_data";
    public const string InsufficientFunds = "insufficient_funds";
    public const string InsufficientPosition = "insufficient_position";
    public const string BelowMinNotional = "below_min_notional";
    public const string TooManyOpenOrders = "too_many_open_orders";

    // Validation errors: the request is refused and nothing is recorded.
    public const string UnknownSymbol = "unknown_symbol";
    public const string InvalidOrderType = "invalid_order_type";
    public const string InvalidQuantity = "invalid_quantity";
    public const string InvalidPrice = "invalid_price";
    public const string LimitPriceRequired = "limit_price_required";
    public const string LimitPriceNotAllowed = "limit_price_not_allowed";
}
