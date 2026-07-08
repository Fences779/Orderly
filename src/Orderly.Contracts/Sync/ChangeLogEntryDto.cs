namespace Orderly.Contracts.Sync;

public sealed class ChangeLogEntryDto
{
    public long Sequence { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public long? Revision { get; set; }
    public long? Version
    {
        get => Revision;
        set => Revision = value;
    }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? PayloadHintJson { get; set; }
}
