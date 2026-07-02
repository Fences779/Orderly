using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Permissions;
using Orderly.Server.Models;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public abstract class CloudControllerBase : ControllerBase
{
    protected ICurrentUserContext CurrentUser { get; }
    protected ICloudAuthService AuthService { get; }
    protected ICloudPermissionService Permissions { get; }

    protected CloudControllerBase(ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
    {
        CurrentUser = currentUser;
        AuthService = authService;
        Permissions = permissions;
    }

    protected Guid UserId => CurrentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");

    protected async Task<CloudWorkspaceMemberRecord> GetMembershipAsync()
    {
        var membership = await AuthService.GetMembershipAsync(UserId);
        if (membership == null || !membership.IsEnabled)
            throw new UnauthorizedAccessException("Membership is not valid.");
        return membership;
    }

    protected Guid WorkspaceIdFromRoute(Guid workspaceId) => workspaceId;

    protected async Task<bool> EnsureWorkspaceAccessAsync(Guid workspaceId)
    {
        var membership = await GetMembershipAsync();
        return membership.WorkspaceId == workspaceId;
    }

    protected static string GetIpAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    protected static string GetUserAgent(HttpContext context) =>
        context.Request.Headers.UserAgent.ToString();
}
