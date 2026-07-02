using Orderly.Contracts.Auth;

namespace Orderly.Remote.Auth;

public sealed class CloudAuthSession
{
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public CloudUserDto User { get; set; } = new();
    public CloudWorkspaceMembershipDto WorkspaceMembership { get; set; } = new();
    public DateTime ServerTimeUtc { get; set; }
    public DateTime TokenAcquiredAtUtc { get; set; }

    public Guid WorkspaceId => WorkspaceMembership.WorkspaceId;
    public Guid UserId => User.Id;
    public string Role => WorkspaceMembership.CloudRole;
}
