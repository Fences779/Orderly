namespace Orderly.Core.Commerce;

/// <summary>
/// An additional contact person for a <see cref="Customer"/>, owned by a single workspace
/// (Req 2.2). The owning <see cref="CustomerId"/> is fixed at creation; the contact's details are
/// mutable and advance <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class CustomerContact : WorkspaceScopedEntity
{
    private string _name = string.Empty;
    private string? _phone;
    private string? _email;
    private string? _role;
    private bool _isPrimary;

    /// <summary>Identity of the owning <see cref="Customer"/>. Fixed at creation.</summary>
    public Guid CustomerId { get; init; }

    /// <summary>Display name of the contact person.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; MarkUpdated(); }
    }

    /// <summary>Optional phone number.</summary>
    public string? Phone
    {
        get => _phone;
        set { _phone = value; MarkUpdated(); }
    }

    /// <summary>Optional email address.</summary>
    public string? Email
    {
        get => _email;
        set { _email = value; MarkUpdated(); }
    }

    /// <summary>Optional role/relationship label for the contact.</summary>
    public string? Role
    {
        get => _role;
        set { _role = value; MarkUpdated(); }
    }

    /// <summary>Whether this is the customer's primary contact.</summary>
    public bool IsPrimary
    {
        get => _isPrimary;
        set { _isPrimary = value; MarkUpdated(); }
    }
}
