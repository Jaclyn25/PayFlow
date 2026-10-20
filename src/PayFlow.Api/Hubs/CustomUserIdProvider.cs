using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace PayFlow.Api.Hubs;

public class CustomUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return GetUserIdFromPrincipal(connection.User);
    }

    public static string? GetUserIdFromPrincipal(ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal?.FindFirst("sub")?.Value;
    }
}
