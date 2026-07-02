namespace Orderly.Server.Models;

public sealed class CloudWorkspaceMemberRecord
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public string CloudRole { get; set; } = string.Empty;
    public string BusinessLabel { get; set; } = string.Empty;
    public int RolePolicyVersion { get; set; }
    public bool IsEnabled { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
