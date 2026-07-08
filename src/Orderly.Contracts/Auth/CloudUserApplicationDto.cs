namespace Orderly.Contracts.Auth;

public sealed class CloudUserApplicationDto
{
    public Guid Id { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? InvitationId { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RequestedDeviceId { get; set; } = string.Empty;
    public string RequestedDeviceName { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string ReviewedByDisplayName { get; set; } = string.Empty;
    public string ReviewReason { get; set; } = string.Empty;
    public Guid? CreatedUserId { get; set; }
}
