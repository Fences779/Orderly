namespace Orderly.Contracts.Auth;

public sealed class CloudInvitationDto
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string CloudRole { get; set; } = string.Empty;
    public string BusinessLabel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int MaxUses { get; set; }
    public int UsedCount { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByDisplayName { get; set; } = string.Empty;
}
