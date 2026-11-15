using PaperTrading.Domain;

namespace PaperTrading.Domain.Matching;

internal sealed class Account(Guid id, decimal initialCash, DateTimeOffset openedAt)
{
    public Guid Id { get; } = id;
    public DateTimeOffset OpenedAt { get; } = openedAt;
    public Portfolio Portfolio { get; } = new(initialCash);
    public Dictionary<Guid, Order> Orders { get; } = [];
    public List<Order> OrderHistory { get; } = [];
    public List<Trade> Trades { get; } = [];
    public Dictionary<string, (PlaceOrderRequest Request, Order Order)> IdempotencyKeys { get; } = new(StringComparer.Ordinal);
    public int OpenOrderCount { get; set; }
}
