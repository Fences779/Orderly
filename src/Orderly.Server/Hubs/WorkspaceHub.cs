using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Orderly.Contracts.Realtime;

namespace Orderly.Server.Hubs;

[Authorize]
public sealed class WorkspaceHub : Hub
{
    public async Task JoinWorkspace(Guid workspaceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, workspaceId.ToString("N"));
    }

    public async Task LeaveWorkspace(Guid workspaceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, workspaceId.ToString("N"));
    }

    public override async Task OnConnectedAsync()
    {
        if (TryGetUserId(out var userId) && TryGetWorkspaceId(out var workspaceId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, workspaceId.ToString("N"));
            await Clients.Group(workspaceId.ToString("N")).SendAsync(RealtimeEvent.UserOnline, new
            {
                WorkspaceId = workspaceId,
                UserId = userId,
                DisplayName = GetDisplayName()
            });
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var userId) && TryGetWorkspaceId(out var workspaceId))
        {
            await Clients.Group(workspaceId.ToString("N")).SendAsync(RealtimeEvent.UserOffline, new
            {
                WorkspaceId = workspaceId,
                UserId = userId,
                DisplayName = GetDisplayName()
            });
        }
        await base.OnDisconnectedAsync(exception);
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? Context.User?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        return !string.IsNullOrEmpty(value) && Guid.TryParse(value, out userId);
    }

    private bool TryGetWorkspaceId(out Guid workspaceId)
    {
        workspaceId = Guid.Empty;
        var value = Context.User?.FindFirst("workspace_id")?.Value;
        return !string.IsNullOrEmpty(value) && Guid.TryParse(value, out workspaceId);
    }

    private string GetDisplayName() =>
        Context.User?.FindFirst(ClaimTypes.Name)?.Value
        ?? Context.User?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Name)?.Value
        ?? string.Empty;
}
