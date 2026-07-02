namespace Orderly.Contracts.Auth;

public sealed class CloudWorkspaceMembershipDto
{
    public Guid WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string CloudRole { get; set; } = string.Empty;
    public string BusinessLabel { get; set; } = string.Empty;
    public int RolePolicyVersion { get; set; }
    public bool IsEnabled { get; set; }
}
