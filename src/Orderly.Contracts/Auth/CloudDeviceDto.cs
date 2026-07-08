namespace Orderly.Contracts.Auth;

public sealed class CloudDeviceDto
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string ApprovedByDisplayName { get; set; } = string.Empty;
    public DateTime? RevokedAtUtc { get; set; }
    public string RevokedByDisplayName { get; set; } = string.Empty;
}
