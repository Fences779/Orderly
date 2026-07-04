namespace Orderly.Server.Models;

public sealed class CloudEmergencyDraftRecord
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public long? BaseRevision { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? LastSubmitError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
}
