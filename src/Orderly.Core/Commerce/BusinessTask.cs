namespace Orderly.Core.Commerce;

/// <summary>
/// A business task or follow-up owned by a single workspace (Req 2.2). Tracks a
/// <see cref="TaskStatus"/> and optional links to a customer or order. Mutable fields advance
/// <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class BusinessTask : WorkspaceScopedEntity
{
    private string _title = string.Empty;
    private string? _description;
    private TaskStatus _status = TaskStatus.Pending;
    private DateTime? _dueDate;
    private DateTime? _completedAt;
    private Guid? _customerId;
    private Guid? _orderId;

    /// <summary>Short title of the task.</summary>
    public string Title
    {
        get => _title;
        set { _title = value; MarkUpdated(); }
    }

    /// <summary>Optional detailed description.</summary>
    public string? Description
    {
        get => _description;
        set { _description = value; MarkUpdated(); }
    }

    /// <summary>Current status of the task.</summary>
    public TaskStatus Status
    {
        get => _status;
        set { _status = value; MarkUpdated(); }
    }

    /// <summary>Optional due date.</summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set { _dueDate = value; MarkUpdated(); }
    }

    /// <summary>Optional completion timestamp.</summary>
    public DateTime? CompletedAt
    {
        get => _completedAt;
        set { _completedAt = value; MarkUpdated(); }
    }

    /// <summary>Optional link to a related <see cref="Customer"/>.</summary>
    public Guid? CustomerId
    {
        get => _customerId;
        set { _customerId = value; MarkUpdated(); }
    }

    /// <summary>Optional link to a related <see cref="Order"/>.</summary>
    public Guid? OrderId
    {
        get => _orderId;
        set { _orderId = value; MarkUpdated(); }
    }
}
