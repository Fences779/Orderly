namespace Orderly.Server.Models;

public sealed class CloudDeviceRecord
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
}
