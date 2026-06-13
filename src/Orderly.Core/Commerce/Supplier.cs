namespace Orderly.Core.Commerce;

/// <summary>
/// A supplier owned by a single workspace (Req 2.2). Industry-agnostic contact fields only;
/// any extra attributes live in <see cref="CommerceEntity.CustomFieldsJson"/> (Req 2.4). Mutable
/// fields advance <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class Supplier : WorkspaceScopedEntity
{
    private string _name = string.Empty;
    private string? _contactName;
    private string? _phone;
    private string? _email;
    private string? _address;
    private string? _note;

    /// <summary>Display name of the supplier.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; MarkUpdated(); }
    }

    /// <summary>Optional primary contact person.</summary>
    public string? ContactName
    {
        get => _contactName;
        set { _contactName = value; MarkUpdated(); }
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

    /// <summary>Optional address.</summary>
    public string? Address
    {
        get => _address;
        set { _address = value; MarkUpdated(); }
    }

    /// <summary>Optional free-text note.</summary>
    public string? Note
    {
        get => _note;
        set { _note = value; MarkUpdated(); }
    }
}
