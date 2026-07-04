namespace Orderly.Contracts.Commerce;

public sealed class BusinessTaskStatusCommand : WriteCommandBase
{
    public Guid TaskId { get; set; }
    public Orderly.Core.Commerce.TaskStatus NewStatus { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
