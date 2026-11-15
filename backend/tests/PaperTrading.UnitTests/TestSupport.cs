using PaperTrading.Domain;
using PaperTrading.Domain.Matching;

namespace PaperTrading.UnitTests;

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

internal static class Instruments
{
    public static readonly Instrument Btc = new("BTCUSDT", "BTC", "USDT", TickSize: 0.01m, StepSize: 0.00001m, MinNotional: 5m);
    public static readonly Instrument Eth = new("ETHUSDT", "ETH", "USDT", TickSize: 0.01m, StepSize: 0.0001m, MinNotional: 5m);
    public static readonly Instrument[] All = [Btc, Eth];
}

/// <summary>Small DSL over the engine to keep tests readable.</summary>
internal sealed class EngineHarness
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public EngineHarness(decimal feeRate = 0.001m, decimal initialCash = 100_000m, int maxOpenOrders = 100, Action<TradingOptions>? configure = null)
    {
        Options = new TradingOptions
        {
            FeeRate = feeRate,
            InitialCash = initialCash,
            MaxQuoteAge = TimeSpan.FromSeconds(10),
            MaxOpenOrdersPerAccount = maxOpenOrders,
        };
        configure?.Invoke(Options);
        Engine = new MatchingEngine(Instruments.All, Options, Time);
    }

    public ManualTimeProvider Time { get; } = new(Start);
    public TradingOptions Options { get; }
    public MatchingEngine Engine { get; }
    public Guid Account { get; } = Guid.NewGuid();

    public void Quote(decimal bid, decimal ask, string symbol = "BTCUSDT") =>
        Engine.ApplyQuote(new Quote(symbol, bid, ask, Time.GetUtcNow()));

    public PlaceOrderResult Market(OrderSide side, decimal qty, string symbol = "BTCUSDT", string? key = null, Guid? account = null) =>
        Engine.PlaceOrder(account ?? Account, new PlaceOrderRequest(symbol, side, OrderType.Market, qty), key);

    public PlaceOrderResult Limit(OrderSide side, decimal qty, decimal price, string symbol = "BTCUSDT", string? key = null, Guid? account = null) =>
        Engine.PlaceOrder(account ?? Account, new PlaceOrderRequest(symbol, side, OrderType.Limit, qty, price), key);

    public PortfolioValuation Portfolio => Engine.GetPortfolio(Account);

    public OrderSnapshot Order(Guid id) => Engine.GetOrder(Account, id) ?? throw new InvalidOperationException("order not found");
}
