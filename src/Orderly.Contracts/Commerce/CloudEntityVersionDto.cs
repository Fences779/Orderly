namespace Orderly.Contracts.Commerce;

public sealed class CloudEntityVersionDto
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public long Revision { get; set; }
    public string Action { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByDisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
