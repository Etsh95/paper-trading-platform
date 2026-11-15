using PaperTrading.Domain;

namespace PaperTrading.Domain.Matching;

/// <summary>
/// Resting limit orders for one symbol, kept in price-time priority.
/// Bids: highest price first; asks: lowest price first; ties broken by arrival sequence (FIFO).
/// Finding triggered orders is O(k log n) for k triggered orders.
/// </summary>
internal sealed class RestingOrderBook
{
    private static readonly Comparer<Order> BidPriority = Comparer<Order>.Create((x, y) =>
    {
        var byPrice = y.LimitPrice!.Value.CompareTo(x.LimitPrice!.Value);
        return byPrice != 0 ? byPrice : x.Sequence.CompareTo(y.Sequence);
    });

    private static readonly Comparer<Order> AskPriority = Comparer<Order>.Create((x, y) =>
    {
        var byPrice = x.LimitPrice!.Value.CompareTo(y.LimitPrice!.Value);
        return byPrice != 0 ? byPrice : x.Sequence.CompareTo(y.Sequence);
    });

    private readonly SortedSet<Order> _bids = new(BidPriority);
    private readonly SortedSet<Order> _asks = new(AskPriority);

    public int Count => _bids.Count + _asks.Count;

    public void Add(Order order)
    {
        if (order.Type != OrderType.Limit || order.LimitPrice is null)
            throw new ArgumentException("Only limit orders can rest on the book.", nameof(order));

        var added = order.Side == OrderSide.Buy ? _bids.Add(order) : _asks.Add(order);
        if (!added) throw new InvalidOperationException($"Order {order.Id} is already on the book.");
    }

    public bool Remove(Order order) =>
        order.Side == OrderSide.Buy ? _bids.Remove(order) : _asks.Remove(order);

    /// <summary>
    /// Removes and returns every order the quote crosses: buys with limit &gt;= ask, sells with limit &lt;= bid.
    /// Returned in arrival order so fills are deterministic.
    /// </summary>
    public List<Order> TakeTriggered(Quote quote)
    {
        var triggered = new List<Order>();

        while (_bids.Min is { } bid && bid.LimitPrice!.Value >= quote.Ask)
        {
            _bids.Remove(bid);
            triggered.Add(bid);
        }

        while (_asks.Min is { } ask && ask.LimitPrice!.Value <= quote.Bid)
        {
            _asks.Remove(ask);
            triggered.Add(ask);
        }

        triggered.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
        return triggered;
    }
}
