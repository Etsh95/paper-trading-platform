using PaperTrading.Core.Domain;
using PaperTrading.Core.Engine;
using static PaperTrading.Core.Domain.OrderSide;

namespace PaperTrading.Core.Tests;

public class MatchingEngineTests
{
    private readonly EngineHarness _h = new();

    // ------------------------------------------------------------ market orders

    [Fact]
    public void Market_buy_fills_at_ask_and_charges_fee()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);

        var result = _h.Market(Buy, 0.1m);

        Assert.Equal(PlaceOrderOutcome.Accepted, result.Outcome);
        Assert.Equal(OrderStatus.Filled, result.Order!.Status);
        Assert.Equal(50_010m, result.Order.FillPrice);
        Assert.Equal(5.001m, result.Order.Fee);
        Assert.Equal(100_000m - 5_001m - 5.001m, _h.Portfolio.Cash);
    }

    [Fact]
    public void Market_sell_fills_at_bid_and_realizes_pnl()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        _h.Market(Buy, 0.1m); // cost basis 5,001 + 5.001 fee = 5,006.001

        _h.Quote(bid: 52_000m, ask: 52_010m);
        var sell = _h.Market(Sell, 0.1m);

        Assert.Equal(52_000m, sell.Order!.FillPrice);
        Assert.Equal(5.2m, sell.Order.Fee);
        var trade = _h.Engine.GetTrades(_h.Account)[0];
        Assert.Equal(5_200m - 5.2m - 5_006.001m, trade.RealizedPnl);
        Assert.Equal(trade.RealizedPnl, _h.Portfolio.RealizedPnl);
    }

    [Fact]
    public void Market_order_without_any_quote_is_rejected()
    {
        var result = _h.Market(Buy, 0.1m);

        Assert.Equal(PlaceOrderOutcome.Rejected, result.Outcome);
        Assert.Equal(RejectReasons.NoMarketData, result.Order!.RejectReason);
        Assert.Equal(100_000m, _h.Portfolio.Cash);
    }

    [Fact]
    public void Market_order_on_stale_quote_is_rejected()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        _h.Time.Advance(TimeSpan.FromSeconds(11));

        var result = _h.Market(Buy, 0.1m);

        Assert.Equal(RejectReasons.StaleMarketData, result.Order!.RejectReason);
    }

    [Fact]
    public void Market_buy_without_enough_cash_is_rejected_and_balance_is_untouched()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);

        var result = _h.Market(Buy, 2m); // 100,020 + fee > 100,000

        Assert.Equal(RejectReasons.InsufficientFunds, result.Order!.RejectReason);
        Assert.Equal(100_000m, _h.Portfolio.Cash);
        Assert.Empty(_h.Engine.GetTrades(_h.Account));
    }

    [Fact]
    public void Buy_that_needs_cash_for_the_fee_is_rejected()
    {
        var h = new EngineHarness(initialCash: 1_000m, feeRate: 0.001m);
        h.Quote(bid: 999m, ask: 1_000m);

        Assert.Equal(RejectReasons.InsufficientFunds, h.Market(Buy, 1m).Order!.RejectReason);
    }

    [Fact]
    public void Selling_without_a_position_is_rejected()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);

        Assert.Equal(RejectReasons.InsufficientPosition, _h.Market(Sell, 0.1m).Order!.RejectReason);
    }

    // ------------------------------------------------------------ limit orders

    [Fact]
    public void Limit_buy_below_the_ask_rests_and_reserves_notional_plus_fee()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);

        var result = _h.Limit(Buy, 0.1m, 49_000m);

        Assert.Equal(OrderStatus.Open, result.Order!.Status);
        Assert.Equal(4_904.9m, _h.Portfolio.ReservedCash); // 4,900 + 4.90 fee
        Assert.Equal(100_000m, _h.Portfolio.Cash);
        Assert.Equal(100_000m - 4_904.9m, _h.Portfolio.AvailableCash);
    }

    [Fact]
    public void Resting_limit_buy_waits_until_ask_reaches_limit_then_fills_at_limit()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        var id = _h.Limit(Buy, 0.1m, 49_000m).Order!.Id;

        _h.Quote(bid: 49_500m, ask: 49_000.01m);
        Assert.Equal(OrderStatus.Open, _h.Order(id).Status);

        _h.Quote(bid: 48_990m, ask: 49_000m);
        var order = _h.Order(id);
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(49_000m, order.FillPrice);
        Assert.Equal(4.9m, order.Fee);
        Assert.Equal(0m, _h.Portfolio.ReservedCash);
        Assert.Equal(100_000m - 4_904.9m, _h.Portfolio.Cash);
    }

    [Fact]
    public void Resting_limit_fills_at_its_limit_even_when_the_market_gaps_through()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        var id = _h.Limit(Buy, 0.1m, 49_000m).Order!.Id;

        _h.Quote(bid: 47_990m, ask: 48_000m);

        Assert.Equal(49_000m, _h.Order(id).FillPrice);
    }

    [Fact]
    public void Marketable_limit_buy_fills_immediately_at_the_better_ask()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);

        var result = _h.Limit(Buy, 0.1m, 51_000m);

        Assert.Equal(OrderStatus.Filled, result.Order!.Status);
        Assert.Equal(50_010m, result.Order.FillPrice);
        Assert.Equal(0m, _h.Portfolio.ReservedCash);
    }

    [Fact]
    public void Limit_order_placed_before_any_quote_rests_and_fills_on_first_crossing_tick()
    {
        var id = _h.Limit(Buy, 0.1m, 49_000m).Order!.Id;
        Assert.Equal(OrderStatus.Open, _h.Order(id).Status);

        _h.Quote(bid: 48_000m, ask: 48_500m);

        Assert.Equal(OrderStatus.Filled, _h.Order(id).Status);
    }

    [Fact]
    public void Resting_limit_sell_fills_when_bid_reaches_limit()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        _h.Market(Buy, 0.1m);
        var id = _h.Limit(Sell, 0.1m, 52_000m).Order!.Id;
        Assert.Equal(0.1m, _h.Portfolio.Positions[0].ReservedQuantity);

        _h.Quote(bid: 51_999.99m, ask: 52_000m);
        Assert.Equal(OrderStatus.Open, _h.Order(id).Status);

        _h.Quote(bid: 52_000m, ask: 52_010m);
        Assert.Equal(OrderStatus.Filled, _h.Order(id).Status);
        Assert.Equal(52_000m, _h.Order(id).FillPrice);
        Assert.Empty(_h.Portfolio.Positions);
    }

    [Fact]
    public void Reservations_prevent_spending_the_same_cash_twice()
    {
        var h = new EngineHarness(initialCash: 10_000m, feeRate: 0m);
        h.Quote(bid: 50_000m, ask: 50_010m);

        var first = h.Limit(Buy, 0.12m, 49_000m);  // reserves 5,880
        var second = h.Limit(Buy, 0.12m, 49_000m); // only 4,120 left

        Assert.Equal(OrderStatus.Open, first.Order!.Status);
        Assert.Equal(RejectReasons.InsufficientFunds, second.Order!.RejectReason);
        Assert.Equal(RejectReasons.InsufficientFunds, h.Market(Buy, 0.1m).Order!.RejectReason);
    }

    [Fact]
    public void Reserved_quantity_cannot_be_sold_twice()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        _h.Market(Buy, 0.1m);
        _h.Limit(Sell, 0.1m, 60_000m);

        Assert.Equal(RejectReasons.InsufficientPosition, _h.Market(Sell, 0.1m).Order!.RejectReason);
        Assert.Equal(RejectReasons.InsufficientPosition, _h.Limit(Sell, 0.05m, 61_000m).Order!.RejectReason);
    }

    [Fact]
    public void Better_priced_buy_triggers_first_and_equal_prices_fill_in_arrival_order()
    {
        var h = new EngineHarness(feeRate: 0m);
        h.Quote(bid: 50_000m, ask: 50_010m);
        var a = h.Limit(Buy, 0.01m, 49_000m).Order!.Id;
        var b = h.Limit(Buy, 0.01m, 49_000m).Order!.Id;
        var c = h.Limit(Buy, 0.01m, 49_500m).Order!.Id;

        h.Quote(bid: 49_400m, ask: 49_500m);
        Assert.Equal([c], h.Engine.GetTrades(h.Account).Select(t => t.OrderId));

        h.Quote(bid: 48_900m, ask: 49_000m);
        var fillOrder = h.Engine.GetTrades(h.Account).Reverse().Select(t => t.OrderId);
        Assert.Equal([c, a, b], fillOrder);
    }

    [Fact]
    public void Quote_for_another_symbol_does_not_trigger_orders()
    {
        var id = _h.Limit(Buy, 0.1m, 49_000m).Order!.Id;

        _h.Quote(bid: 1_000m, ask: 1_001m, symbol: "ETHUSDT");

        Assert.Equal(OrderStatus.Open, _h.Order(id).Status);
    }

    [Fact]
    public void Crossed_or_non_positive_quotes_are_ignored()
    {
        var id = _h.Limit(Buy, 0.1m, 49_000m).Order!.Id;

        _h.Quote(bid: 48_000m, ask: 47_000m); // crossed
        _h.Quote(bid: 0m, ask: 1m);

        Assert.Equal(OrderStatus.Open, _h.Order(id).Status);
        Assert.Null(_h.Engine.GetQuote("BTCUSDT"));
    }

    [Fact]
    public void Too_many_open_orders_is_rejected()
    {
        var h = new EngineHarness(maxOpenOrders: 2);
        h.Limit(Buy, 0.001m, 10_000m);
        h.Limit(Buy, 0.001m, 10_000m);

        Assert.Equal(RejectReasons.TooManyOpenOrders, h.Limit(Buy, 0.001m, 10_000m).Order!.RejectReason);
    }

    // ------------------------------------------------------------ cancel

    [Fact]
    public void Cancel_releases_reserved_cash_and_order_no_longer_fills()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        var id = _h.Limit(Buy, 0.1m, 49_000m).Order!.Id;

        var result = _h.Engine.CancelOrder(_h.Account, id);
        _h.Quote(bid: 48_000m, ask: 48_010m);

        Assert.Equal(CancelOrderOutcome.Cancelled, result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, _h.Order(id).Status);
        Assert.Equal(0m, _h.Portfolio.ReservedCash);
        Assert.Equal(100_000m, _h.Portfolio.Cash);
        Assert.Empty(_h.Engine.GetTrades(_h.Account));
    }

    [Fact]
    public void Cancel_releases_reserved_quantity()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        _h.Market(Buy, 0.1m);
        var id = _h.Limit(Sell, 0.1m, 60_000m).Order!.Id;

        _h.Engine.CancelOrder(_h.Account, id);

        Assert.Equal(0m, _h.Portfolio.Positions[0].ReservedQuantity);
        Assert.Equal(PlaceOrderOutcome.Accepted, _h.Market(Sell, 0.1m).Outcome);
    }

    [Fact]
    public void Filled_orders_cannot_be_cancelled_and_unknown_orders_are_not_found()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        var filled = _h.Market(Buy, 0.1m).Order!.Id;

        Assert.Equal(CancelOrderOutcome.NotCancellable, _h.Engine.CancelOrder(_h.Account, filled).Outcome);
        Assert.Equal(CancelOrderOutcome.NotFound, _h.Engine.CancelOrder(_h.Account, Guid.NewGuid()).Outcome);
    }

    [Fact]
    public void Accounts_cannot_cancel_each_others_orders()
    {
        var id = _h.Limit(Buy, 0.1m, 49_000m).Order!.Id;

        Assert.Equal(CancelOrderOutcome.NotFound, _h.Engine.CancelOrder(Guid.NewGuid(), id).Outcome);
        Assert.Equal(OrderStatus.Open, _h.Order(id).Status);
    }

    // ------------------------------------------------------------ validation

    public static TheoryData<PlaceOrderRequest, string> InvalidRequests => new()
    {
        { new("DOGEUSDT", Buy, OrderType.Market, 1m), RejectReasons.UnknownSymbol },
        { new("BTCUSDT", Buy, OrderType.Market, 0m), RejectReasons.InvalidQuantity },
        { new("BTCUSDT", Buy, OrderType.Market, -1m), RejectReasons.InvalidQuantity },
        { new("BTCUSDT", Buy, OrderType.Market, 0.000001m), RejectReasons.InvalidQuantity }, // finer than step 0.00001
        { new("BTCUSDT", Buy, OrderType.Market, 0.1m, 50_000m), RejectReasons.LimitPriceNotAllowed },
        { new("BTCUSDT", Buy, OrderType.Limit, 0.1m), RejectReasons.LimitPriceRequired },
        { new("BTCUSDT", Buy, OrderType.Limit, 0.1m, 50_000.001m), RejectReasons.InvalidPrice }, // finer than tick 0.01
        { new("BTCUSDT", Buy, OrderType.Limit, 0.1m, 0m), RejectReasons.InvalidPrice },
        { new("BTCUSDT", Buy, OrderType.Limit, 0.0001m, 100m), RejectReasons.BelowMinNotional },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Invalid_requests_are_refused_without_recording_an_order(PlaceOrderRequest request, string code)
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);

        var result = _h.Engine.PlaceOrder(_h.Account, request);

        Assert.Equal(PlaceOrderOutcome.Invalid, result.Outcome);
        Assert.Equal(code, result.ErrorCode);
        Assert.Empty(_h.Engine.GetOrders(_h.Account));
    }

    [Fact]
    public void Market_order_below_min_notional_at_current_price_is_rejected()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);

        Assert.Equal(RejectReasons.BelowMinNotional, _h.Market(Buy, 0.00001m).Order!.RejectReason); // ~0.50 USDT
    }

    [Fact]
    public void Symbol_is_case_insensitive()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);

        Assert.Equal("BTCUSDT", _h.Market(Buy, 0.1m, symbol: " btcusdt ").Order!.Symbol);
    }

    // ------------------------------------------------------------ idempotency

    [Fact]
    public void Same_idempotency_key_and_request_replays_the_original_order_without_executing_twice()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);

        var first = _h.Market(Buy, 0.1m, key: "k-1");
        var cashAfterFirst = _h.Portfolio.Cash;
        var second = _h.Market(Buy, 0.1m, key: "k-1");

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Order!.Id, second.Order!.Id);
        Assert.Single(_h.Engine.GetTrades(_h.Account));
        Assert.Equal(cashAfterFirst, _h.Portfolio.Cash);
    }

    [Fact]
    public void Replay_of_a_rejected_order_returns_the_same_rejection()
    {
        var first = _h.Market(Buy, 0.1m, key: "k-1"); // no quote yet
        _h.Quote(bid: 50_000m, ask: 50_010m);
        var second = _h.Market(Buy, 0.1m, key: "k-1"); // would now succeed, but must not re-execute

        Assert.Equal(PlaceOrderOutcome.Rejected, second.Outcome);
        Assert.Equal(first.Order!.Id, second.Order!.Id);
        Assert.True(second.Replayed);
    }

    [Fact]
    public void Same_key_with_a_different_request_is_a_conflict()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        _h.Market(Buy, 0.1m, key: "k-1");

        var conflict = _h.Market(Buy, 0.2m, key: "k-1");

        Assert.Equal(PlaceOrderOutcome.IdempotencyConflict, conflict.Outcome);
        Assert.Single(_h.Engine.GetTrades(_h.Account));
    }

    [Fact]
    public void Equivalent_requests_match_regardless_of_decimal_scale_and_symbol_case()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        var first = _h.Market(Buy, 0.1m, key: "k-1");

        var second = _h.Market(Buy, 0.10000m, symbol: "btcusdt", key: "k-1");

        Assert.True(second.Replayed);
        Assert.Equal(first.Order!.Id, second.Order!.Id);
    }

    [Fact]
    public void Invalid_request_does_not_consume_the_idempotency_key()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        Assert.Equal(PlaceOrderOutcome.Invalid, _h.Market(Buy, 0m, key: "k-1").Outcome);

        var retry = _h.Market(Buy, 0.1m, key: "k-1");

        Assert.Equal(PlaceOrderOutcome.Accepted, retry.Outcome);
        Assert.False(retry.Replayed);
    }

    [Fact]
    public void Idempotency_keys_are_scoped_per_account()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        var other = Guid.NewGuid();

        var mine = _h.Market(Buy, 0.1m, key: "shared");
        var theirs = _h.Market(Buy, 0.1m, key: "shared", account: other);

        Assert.False(theirs.Replayed);
        Assert.NotEqual(mine.Order!.Id, theirs.Order!.Id);
    }

    // ------------------------------------------------------------ events

    [Fact]
    public void Fills_emit_trade_and_order_events_for_the_owning_account()
    {
        _h.Quote(bid: 50_000m, ask: 50_010m);
        _h.Engine.DrainEvents();

        _h.Market(Buy, 0.1m);
        var events = _h.Engine.DrainEvents();

        Assert.Collection(events,
            e => Assert.IsType<TradeExecuted>(e),
            e => Assert.Equal(OrderStatus.Filled, Assert.IsType<OrderUpdated>(e).Order.Status));
        Assert.All(events, e => Assert.Equal(_h.Account, e.AccountId));
        Assert.Empty(_h.Engine.DrainEvents());
    }
}
