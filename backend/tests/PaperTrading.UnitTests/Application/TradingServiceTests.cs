using PaperTrading.Application.Trading;
using PaperTrading.Domain;
using PaperTrading.Domain.Matching;

namespace PaperTrading.UnitTests.Application;

public class TradingServiceTests : IAsyncLifetime
{
    private readonly ManualTimeProvider _time = new(EngineHarness.Start);
    private EngineSequencer _sequencer = null!;
    private TradingService _service = null!;

    public Task InitializeAsync()
    {
        var engine = new MatchingEngine(Instruments.All, new TradingOptions { FeeRate = 0m }, _time);
        _sequencer = new EngineSequencer(engine);
        _service = new TradingService(_sequencer);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _sequencer.DisposeAsync();

    [Fact]
    public async Task Use_cases_run_against_one_consistent_engine()
    {
        var account = Guid.NewGuid();
        await _sequencer.PublishQuoteAsync(new Quote("BTCUSDT", 999m, 1_000m, _time.GetUtcNow()));

        var opened = await _service.OpenAccountAsync(account);
        var placed = await _service.PlaceOrderAsync(account, new PlaceOrderRequest("BTCUSDT", OrderSide.Buy, OrderType.Market, 1m), "k1");
        var portfolio = await _service.GetPortfolioAsync(account);
        var trades = await _service.GetTradesAsync(account, 10);
        var orders = await _service.GetOrdersAsync(account, OrderStatus.Filled, 10);

        Assert.Equal(100_000m, opened!.Cash);
        Assert.Equal(OrderStatus.Filled, placed.Order!.Status);
        Assert.Equal(99_000m, portfolio.Cash);
        Assert.Single(trades);
        Assert.Equal(placed.Order.Id, Assert.Single(orders).Id);
        Assert.Equal(placed.Order.Id, (await _service.GetOrderAsync(account, placed.Order.Id))!.Id);
    }

    [Fact]
    public async Task Execution_quote_reflects_what_the_engine_has_applied()
    {
        Assert.Null(await _service.GetExecutionQuoteAsync("BTCUSDT"));

        var quote = new Quote("BTCUSDT", 999m, 1_000m, _time.GetUtcNow());
        await _sequencer.PublishQuoteAsync(quote);

        Assert.Equal(quote, await _service.GetExecutionQuoteAsync("BTCUSDT"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Placing_an_order_requires_an_idempotency_key(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.PlaceOrderAsync(Guid.NewGuid(), new PlaceOrderRequest("BTCUSDT", OrderSide.Buy, OrderType.Market, 1m), key));
    }
}
