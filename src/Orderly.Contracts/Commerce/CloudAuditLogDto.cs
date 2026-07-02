namespace Orderly.Contracts.Commerce;

public sealed class CloudAuditLogDto
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorDisplayName { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Reason { get; set; }
    public string? ClientRequestId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
