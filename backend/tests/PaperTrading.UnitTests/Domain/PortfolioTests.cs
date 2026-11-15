using PaperTrading.Domain;

namespace PaperTrading.UnitTests.Domain;

public class PortfolioTests
{
    private static readonly DateTimeOffset Now = EngineHarness.Start;
    private const string Btc = "BTCUSDT";

    [Fact]
    public void Buy_debits_notional_plus_fee_and_capitalises_fee_into_cost_basis()
    {
        var p = new Portfolio(100_000m);

        p.ApplyBuy(Btc, 0.5m, 40_000m, fee: 20m);

        Assert.Equal(79_980m, p.Cash);
        Assert.Equal(0.5m, p.Positions[Btc].Quantity);
        Assert.Equal(20_020m, p.Positions[Btc].CostBasis);
        Assert.Equal(40_040m, p.Positions[Btc].AverageCost);
        Assert.Equal(20m, p.TotalFees);
        Assert.Equal(0m, p.RealizedPnl);
    }

    [Fact]
    public void Average_cost_is_weighted_across_buys()
    {
        var p = new Portfolio(100_000m);
        p.ApplyBuy(Btc, 0.5m, 40_000m, fee: 20m);
        p.ApplyBuy(Btc, 0.5m, 42_000m, fee: 21m);

        Assert.Equal(1m, p.Positions[Btc].Quantity);
        Assert.Equal(41_041m, p.Positions[Btc].CostBasis);
        Assert.Equal(41_041m, p.Positions[Btc].AverageCost);
        Assert.Equal(58_959m, p.Cash);
    }

    [Fact]
    public void Partial_sell_realizes_pro_rata_pnl_net_of_fee_then_full_close_realizes_the_rest()
    {
        var p = new Portfolio(100_000m);
        p.ApplyBuy(Btc, 0.5m, 40_000m, fee: 20m);
        p.ApplyBuy(Btc, 0.5m, 42_000m, fee: 21m);

        var first = p.ApplySell(Btc, 0.25m, 45_000m, fee: 11.25m);

        // proceeds 11,250 - fee 11.25 - cost relieved (41,041 * 0.25) 10,260.25
        Assert.Equal(978.50m, first);
        Assert.Equal(0.75m, p.Positions[Btc].Quantity);
        Assert.Equal(30_780.75m, p.Positions[Btc].CostBasis);

        var second = p.ApplySell(Btc, 0.75m, 44_000m, fee: 33m);

        Assert.Equal(2_186.25m, second);
        Assert.Equal(0m, p.Positions[Btc].Quantity);
        Assert.Equal(0m, p.Positions[Btc].CostBasis);
        Assert.Equal(3_164.75m, p.RealizedPnl);
        Assert.Equal(p.InitialCash + p.RealizedPnl, p.Cash);
    }

    [Fact]
    public void Closing_in_thirds_leaves_no_rounding_residue()
    {
        var p = new Portfolio(1_000m);
        p.ApplyBuy(Btc, 3m, 33.33m, fee: 0.01m); // cost basis 99.99 + 0.01 = 100 exactly
        Assert.Equal(100m, p.Positions[Btc].CostBasis);

        p.ApplySell(Btc, 1m, 40m, fee: 0m); // relieves 33.33333333
        p.ApplySell(Btc, 1m, 40m, fee: 0m); // relieves 33.33333334 (banker's rounding of 33.333333335)
        p.ApplySell(Btc, 1m, 40m, fee: 0m); // full close relieves the exact remainder

        Assert.Equal(0m, p.Positions[Btc].CostBasis);
        Assert.Equal(120m - 100m, p.RealizedPnl);
        Assert.Equal(p.InitialCash + p.RealizedPnl, p.Cash);
    }

    [Fact]
    public void Valuation_marks_long_positions_at_bid_and_satisfies_equity_identity()
    {
        var p = new Portfolio(100_000m);
        p.ApplyBuy(Btc, 1m, 41_000m, fee: 41m);

        var v = p.Valuate(_ => new Quote(Btc, Bid: 43_000m, Ask: 43_010m, Now), Now);

        var row = Assert.Single(v.Positions);
        Assert.Equal(43_000m, row.MarkPrice);
        Assert.Equal(43_000m, row.MarketValue);
        Assert.Equal(1_959m, row.UnrealizedPnl);
        Assert.Equal(1_959m, v.UnrealizedPnl);
        Assert.Equal(v.Cash + 43_000m, v.Equity);
        Assert.Equal(v.InitialCash + v.TotalPnl, v.Equity);
    }

    [Fact]
    public void Valuation_without_quote_carries_position_at_cost()
    {
        var p = new Portfolio(100_000m);
        p.ApplyBuy(Btc, 1m, 41_000m, fee: 41m);

        var v = p.Valuate(_ => null, Now);

        var row = Assert.Single(v.Positions);
        Assert.Null(row.MarkPrice);
        Assert.Equal(41_041m, row.MarketValue);
        Assert.Equal(0m, row.UnrealizedPnl);
    }

    [Fact]
    public void Closed_positions_are_not_listed_in_valuation()
    {
        var p = new Portfolio(100_000m);
        p.ApplyBuy(Btc, 1m, 100m, fee: 0m);
        p.ApplySell(Btc, 1m, 110m, fee: 0m);

        Assert.Empty(p.Valuate(_ => null, Now).Positions);
    }

    [Fact]
    public void Cash_reservation_reduces_available_cash_and_can_be_released()
    {
        var p = new Portfolio(1_000m);

        Assert.True(p.TryReserveCash(600m));
        Assert.False(p.TryReserveCash(600m));
        Assert.Equal(400m, p.AvailableCash);
        Assert.Equal(1_000m, p.Cash);

        p.ReleaseCash(600m);
        Assert.Equal(1_000m, p.AvailableCash);
    }

    [Fact]
    public void Releasing_more_than_reserved_throws()
    {
        var p = new Portfolio(1_000m);
        p.TryReserveCash(10m);

        Assert.Throws<InvalidOperationException>(() => p.ReleaseCash(11m));
    }

    [Fact]
    public void Buy_beyond_available_cash_throws_and_leaves_state_untouched()
    {
        var p = new Portfolio(1_000m);
        p.TryReserveCash(500m);

        Assert.Throws<InvalidOperationException>(() => p.ApplyBuy(Btc, 1m, 600m, fee: 0m));
        Assert.Equal(1_000m, p.Cash);
        Assert.Equal(500m, p.ReservedCash);
    }

    [Fact]
    public void Selling_more_than_held_or_reserved_quantity_throws()
    {
        var p = new Portfolio(1_000m);
        p.ApplyBuy(Btc, 1m, 100m, fee: 0m);
        Assert.True(p.TryReserveQuantity(Btc, 0.6m));

        Assert.Throws<InvalidOperationException>(() => p.ApplySell(Btc, 0.5m, 100m, fee: 0m));
        Assert.Throws<InvalidOperationException>(() => p.ApplySell("ETHUSDT", 1m, 100m, fee: 0m));
        Assert.Equal(0.4m, p.AvailableQuantity(Btc));
    }

    [Theory]
    [InlineData("5001", "0.001", "5.001")]
    [InlineData("1.23456789", "0.001", "0.00123457")] // 0.00123456789 rounds UP
    [InlineData("0.00000001", "0.001", "0.00000001")] // dust never rounds to a free trade
    [InlineData("100", "0", "0")]
    public void Fee_is_rounded_up_to_eight_decimals(string notional, string rate, string expected)
    {
        static decimal D(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(D(expected), Money.Fee(D(notional), D(rate)));
    }
}
