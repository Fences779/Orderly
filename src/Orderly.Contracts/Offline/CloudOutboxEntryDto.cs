namespace Orderly.Contracts.Offline;

public sealed class CloudOutboxEntryDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public long? BaseRevision { get; set; }
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = CloudOutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LastSubmitError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
