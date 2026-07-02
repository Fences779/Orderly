namespace Orderly.Contracts.Realtime;

public sealed class RealtimeEventPayload
{
    public Guid WorkspaceId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public long? Revision { get; set; }
    public long? Sequence { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorDisplayName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? HintJson { get; set; }
}
