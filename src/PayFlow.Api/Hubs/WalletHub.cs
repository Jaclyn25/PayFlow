using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PayFlow.Application.Common.Interfaces;

namespace PayFlow.Api.Hubs;

[Authorize]
public class WalletHub : Hub<IWalletHubClient>
{
    public const string HubUrl = "/hubs/wallet";

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GetUserGroupName(userId));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetUserGroupName(userId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public Task<string> Ping()
    {
        return Task.FromResult("pong");
    }

    private string? GetUserId()
    {
        return Context.UserIdentifier 
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
    }

    public static string GetUserGroupName(string userId) => $"user_{userId}";
    public static string GetUserGroupName(Guid userId) => $"user_{userId}";
}
