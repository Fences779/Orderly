namespace Orderly.Core.Commerce;

/// <summary>
/// A customer owned by a single workspace (Req 2.2). Carries the contact fields used as
/// deterministic import match keys (Phone → WeChat → Name, design Import section) and the rolled-up
/// RFM statistics maintained by order completion (Req 4.6, 4.11). Mutable fields advance
/// <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class Customer : WorkspaceScopedEntity
{
    private string _name = string.Empty;
    private string? _phone;
    private string? _weChat;
    private string? _email;
    private DateTime? _lastOrderAt;
    private int _completedOrderCount;
    private CommerceMoney _totalSpend = CommerceMoney.Zero;

    /// <summary>Display name of the customer.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; MarkUpdated(); }
    }

    /// <summary>Optional phone number; first import match key for customers.</summary>
    public string? Phone
    {
        get => _phone;
        set { _phone = value; MarkUpdated(); }
    }

    /// <summary>Optional WeChat handle; second import match key for customers.</summary>
    public string? WeChat
    {
        get => _weChat;
        set { _weChat = value; MarkUpdated(); }
    }

    /// <summary>Optional email address.</summary>
    public string? Email
    {
        get => _email;
        set { _email = value; MarkUpdated(); }
    }

    /// <summary>Recency anchor: timestamp of the last completed order, or null when none (Req 4.11).</summary>
    public DateTime? LastOrderAt
    {
        get => _lastOrderAt;
        set { _lastOrderAt = value; MarkUpdated(); }
    }

    /// <summary>Frequency metric: count of completed orders (Req 4.11).</summary>
    public int CompletedOrderCount
    {
        get => _completedOrderCount;
        set { _completedOrderCount = value; MarkUpdated(); }
    }

    /// <summary>Monetary metric: summed total of completed orders (Req 4.11).</summary>
    public CommerceMoney TotalSpend
    {
        get => _totalSpend;
        set { _totalSpend = value; MarkUpdated(); }
    }
}
