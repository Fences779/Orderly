using Microsoft.AspNetCore.SignalR;
using Orderly.Contracts.Realtime;
using Orderly.Server.Hubs;

namespace Orderly.Server.Services;

public sealed class SignalRNotifier : ISignalRNotifier
{
    private readonly IHubContext<WorkspaceHub> _hubContext;

    public SignalRNotifier(IHubContext<WorkspaceHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyAsync(Guid workspaceId, string eventName, RealtimeEventPayload payload)
    {
        await _hubContext.Clients.Group(workspaceId.ToString("N")).SendAsync(eventName, payload);
    }

    public async Task NotifyUserDisabledAsync(Guid userId)
    {
        // Broadcast a force-logout event to all connections of the user.
        await _hubContext.Clients.User(userId.ToString("N")).SendAsync("ForceLogout", new { Reason = "AccountDisabled" });
    }
}
