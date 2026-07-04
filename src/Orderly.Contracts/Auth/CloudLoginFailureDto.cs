namespace Orderly.Contracts.Auth;

public sealed class CloudLoginFailureDto
{
    public Guid Id { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? ClientRequestId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
