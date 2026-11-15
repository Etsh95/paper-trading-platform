using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using PaperTrading.Api.Contracts;
using PaperTrading.Application.Abstractions;
using PaperTrading.Domain.Matching;

namespace PaperTrading.Api.Realtime;

/// <summary>
/// Bridges engine events to SignalR. <see cref="Publish"/> runs on the engine thread and only enqueues;
/// network I/O happens on this background service so a slow client can never stall matching.
/// </summary>
public sealed class SignalREventSink(IHubContext<MarketHub> hub, ILogger<SignalREventSink> logger) : BackgroundService, IEngineEventSink
{
    private readonly Channel<EngineEvent> _events = Channel.CreateBounded<EngineEvent>(
        new BoundedChannelOptions(50_000) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });

    public void Publish(IReadOnlyList<EngineEvent> events)
    {
        foreach (var e in events) _events.Writer.TryWrite(e);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var e in _events.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var group = hub.Clients.Group(MarketHub.AccountGroup(e.AccountId));
                await (e switch
                {
                    OrderUpdated o => group.SendAsync("orderUpdated", o.Order.ToResponse(), stoppingToken),
                    TradeExecuted t => group.SendAsync("tradeExecuted", t.Trade.ToResponse(), stoppingToken),
                    _ => Task.CompletedTask,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to push {Event}", e.GetType().Name);
            }
        }
    }
}
