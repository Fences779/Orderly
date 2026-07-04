namespace Orderly.Contracts.Commerce;

public sealed class UpdateBusinessTaskStatusCommand : WriteCommandBase
{
    public Guid BusinessTaskId { get; set; }
    public Orderly.Core.Commerce.TaskStatus NewStatus { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
