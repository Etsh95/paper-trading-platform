using System.Collections.Concurrent;
using PaperTrading.Core.Domain;
using PaperTrading.Core.Engine;

namespace PaperTrading.Core.Tests;

public class EngineSequencerTests
{
    private static (EngineSequencer Sequencer, MatchingEngine Engine, ManualTimeProvider Time, RecordingSink Sink) Create(decimal initialCash = 100_000m)
    {
        var time = new ManualTimeProvider(EngineHarness.Start);
        var engine = new MatchingEngine(Instruments.All, new TradingOptions { InitialCash = initialCash, FeeRate = 0m }, time);
        var sink = new RecordingSink();
        return (new EngineSequencer(engine, sink), engine, time, sink);
    }

    [Fact]
    public async Task Concurrent_market_buys_never_overspend()
    {
        var (sequencer, _, time, _) = Create(initialCash: 100_000m);
        await using var _s = sequencer;
        await sequencer.PublishQuoteAsync(new Quote("BTCUSDT", 999m, 1_000m, time.GetUtcNow()));
        var account = Guid.NewGuid();

        // 250 orders of 1,000 USDT each race for 100,000 USDT: exactly 100 can succeed.
        var results = await Task.WhenAll(Enumerable.Range(0, 250).Select(_ => Task.Run(() =>
            sequencer.ExecuteAsync(e => e.PlaceOrder(account, new PlaceOrderRequest("BTCUSDT", OrderSide.Buy, OrderType.Market, 1m))))));

        Assert.Equal(100, results.Count(r => r.Order!.Status == OrderStatus.Filled));
        Assert.Equal(150, results.Count(r => r.Order!.RejectReason == RejectReasons.InsufficientFunds));
        var portfolio = await sequencer.ExecuteAsync(e => e.GetPortfolio(account));
        Assert.Equal(0m, portfolio.Cash);
        Assert.Equal(100m, portfolio.Positions.Single().Quantity);
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_idempotency_key_create_exactly_one_order()
    {
        var (sequencer, _, time, _) = Create();
        await using var _s = sequencer;
        await sequencer.PublishQuoteAsync(new Quote("BTCUSDT", 999m, 1_000m, time.GetUtcNow()));
        var account = Guid.NewGuid();
        var request = new PlaceOrderRequest("BTCUSDT", OrderSide.Buy, OrderType.Market, 1m);

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            sequencer.ExecuteAsync(e => e.PlaceOrder(account, request, "same-key")))));

        Assert.Single(results.Select(r => r.Order!.Id).Distinct());
        Assert.Equal(1, results.Count(r => !r.Replayed));
        Assert.Single(await sequencer.ExecuteAsync(e => e.GetTrades(account)));
    }

    [Fact]
    public async Task Orders_racing_price_ticks_keep_reservations_consistent()
    {
        var (sequencer, engine, time, _) = Create(initialCash: 1_000_000m);
        await using var _s = sequencer;
        var account = Guid.NewGuid();

        var ticks = Task.Run(async () =>
        {
            for (var i = 0; i < 2_000; i++)
            {
                var bid = 900m + i % 200;
                await sequencer.PublishQuoteAsync(new Quote("BTCUSDT", bid, bid + 1m, time.GetUtcNow()));
            }
        });
        var orders = Enumerable.Range(0, 400).Select(i => Task.Run(() =>
            sequencer.ExecuteAsync(e => e.PlaceOrder(account,
                new PlaceOrderRequest("BTCUSDT", OrderSide.Buy, OrderType.Limit, 1m, 900m + i % 200))))).ToArray();
        await Task.WhenAll(orders.Append(ticks));

        var (reserved, held, cash, initial) = await sequencer.ExecuteAsync(e =>
        {
            var p = e.FindPortfolio(account)!;
            return (p.ReservedCash, e.OpenOrders(account).Sum(o => o.ReservedCash), p.Cash, p.InitialCash);
        });
        Assert.Equal(held, reserved);
        var filled = (await sequencer.ExecuteAsync(e => e.GetTrades(account, int.MaxValue))).Sum(t => t.Notional);
        Assert.Equal(initial - filled, cash);
        _ = engine;
    }

    [Fact]
    public async Task Engine_events_are_published_to_the_sink()
    {
        var (sequencer, _, time, sink) = Create();
        await using var _s = sequencer;
        await sequencer.PublishQuoteAsync(new Quote("BTCUSDT", 999m, 1_000m, time.GetUtcNow()));
        var account = Guid.NewGuid();

        await sequencer.ExecuteAsync(e => e.PlaceOrder(account, new PlaceOrderRequest("BTCUSDT", OrderSide.Buy, OrderType.Market, 1m)));

        Assert.Contains(sink.Events, e => e is TradeExecuted t && t.AccountId == account);
    }

    [Fact]
    public async Task Exception_in_an_operation_faults_only_that_caller()
    {
        var (sequencer, _, _, _) = Create();
        await using var _s = sequencer;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sequencer.ExecuteAsync<int>(_ => throw new InvalidOperationException("boom")));

        Assert.Equal(100_000m, (await sequencer.ExecuteAsync(e => e.GetPortfolio(Guid.NewGuid()))).Cash);
    }

    private sealed class RecordingSink : IEngineEventSink
    {
        public ConcurrentQueue<EngineEvent> Events { get; } = new();

        public void Publish(IReadOnlyList<EngineEvent> events)
        {
            foreach (var e in events) Events.Enqueue(e);
        }
    }
}
