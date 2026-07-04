namespace Orderly.Contracts.Commerce;

public sealed class CloudBusinessTaskDto : CloudEntityDto
{
    public Guid WorkspaceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Orderly.Core.Commerce.TaskStatus Status { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? AssignedToUserId { get; set; }
}
