using PaperTrading.Domain;
using PaperTrading.Domain.Matching;

namespace PaperTrading.UnitTests.Domain;

/// <summary>
/// Randomized (but seeded, so reproducible) sequences of orders, cancels and price ticks across several accounts.
/// After every step the accounting invariants must hold <b>exactly</b>; any drift would show up as a failure.
/// </summary>
public class AccountingInvariantTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(2026)]
    public void Accounting_invariants_hold_after_every_operation(int seed)
    {
        var rng = new Random(seed);
        var h = new EngineHarness(feeRate: 0.00075m, initialCash: 50_000m);
        Guid[] accounts = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var mids = new Dictionary<string, decimal> { ["BTCUSDT"] = 60_000m, ["ETHUSDT"] = 3_000m };

        for (var step = 0; step < 4_000; step++)
        {
            var account = accounts[rng.Next(accounts.Length)];
            var instrument = Instruments.All[rng.Next(Instruments.All.Length)];

            switch (rng.Next(10))
            {
                case < 4: // price tick: random walk on tick grid
                    var mid = mids[instrument.Symbol] * (1 + (decimal)(rng.NextDouble() - 0.5) / 50);
                    mid = Math.Round(mid / instrument.TickSize) * instrument.TickSize;
                    mids[instrument.Symbol] = mid;
                    h.Engine.ApplyQuote(new Quote(instrument.Symbol, mid, mid + instrument.TickSize * rng.Next(1, 20), h.Time.GetUtcNow()));
                    break;

                case < 9: // order
                    var qty = instrument.StepSize * rng.Next(1, 20_000);
                    var side = rng.Next(2) == 0 ? OrderSide.Buy : OrderSide.Sell;
                    var request = rng.Next(2) == 0
                        ? new PlaceOrderRequest(instrument.Symbol, side, OrderType.Market, qty)
                        : new PlaceOrderRequest(instrument.Symbol, side, OrderType.Limit, qty,
                            Math.Round(mids[instrument.Symbol] * (decimal)(0.97 + rng.NextDouble() * 0.06) / instrument.TickSize) * instrument.TickSize);
                    h.Engine.PlaceOrder(account, request, idempotencyKey: rng.Next(5) == 0 ? $"k{rng.Next(50)}" : null);
                    break;

                default: // cancel a random open order
                    var open = h.Engine.OpenOrders(account).ToList();
                    if (open.Count > 0) h.Engine.CancelOrder(account, open[rng.Next(open.Count)].Id);
                    break;
            }

            h.Time.Advance(TimeSpan.FromMilliseconds(200));
            foreach (var a in accounts) AssertInvariants(h, a);
        }

        Assert.True(h.Engine.GetTrades(accounts[0]).Count > 50, "sanity: the simulation should actually trade");
    }

    private static void AssertInvariants(EngineHarness h, Guid accountId)
    {
        var p = h.Engine.FindPortfolio(accountId);
        if (p is null) return;
        var open = h.Engine.OpenOrders(accountId).ToList();

        // 1. Money is conserved: every unit of cash spent sits in a cost basis, or was realized as P&L.
        Assert.Equal(p.InitialCash + p.RealizedPnl, p.Cash + p.Positions.Values.Sum(x => x.CostBasis));

        // 2. Nothing goes negative.
        Assert.True(p.Cash >= 0);
        Assert.True(p.AvailableCash >= 0);
        Assert.All(p.Positions.Values, x =>
        {
            Assert.True(x.Quantity >= 0);
            Assert.True(x.AvailableQuantity >= 0);
            Assert.True(x.CostBasis >= 0);
        });

        // 3. Reservations equal exactly what open orders hold.
        Assert.Equal(open.Sum(o => o.ReservedCash), p.ReservedCash);
        foreach (var x in p.Positions.Values)
            Assert.Equal(open.Where(o => o.Symbol == x.Symbol).Sum(o => o.ReservedQuantity), x.ReservedQuantity);

        // 4. Fees recorded on trades equal fees charged to the portfolio.
        Assert.Equal(h.Engine.GetTrades(accountId, int.MaxValue).Sum(t => t.Fee), p.TotalFees);

        // 5. Equity identity on the valuation.
        var v = h.Engine.GetPortfolio(accountId);
        Assert.Equal(v.InitialCash + v.TotalPnl, v.Equity);
    }
}
