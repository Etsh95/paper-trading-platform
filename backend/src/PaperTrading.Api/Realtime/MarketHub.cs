using Microsoft.AspNetCore.SignalR;

namespace PaperTrading.Api.Realtime;

/// <summary>
/// Server-to-client only. Everyone receives <c>quotes</c>; authenticated connections (JWT via
/// <c>access_token</c> query string) also join their account group for <c>orderUpdated</c> / <c>tradeExecuted</c>.
/// </summary>
public sealed class MarketHub : Hub
{
    public const string Path = "/hubs/market";

    public static string AccountGroup(Guid accountId) => $"account:{accountId:N}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var accountId))
            await Groups.AddToGroupAsync(Context.ConnectionId, AccountGroup(accountId));

        await base.OnConnectedAsync();
    }
}
