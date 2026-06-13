namespace Orderly.Core.Commerce;

/// <summary>
/// Defines a single user-defined custom field for one entity type within one template (Req 5.4).
/// It is template-scoped (by <see cref="TemplateId"/>) rather than directly workspace-scoped, so it
/// extends <see cref="SystemEntity"/>. Each definition targets exactly one
/// <see cref="BusinessEntityType"/> and has exactly one <see cref="CustomFieldDataType"/>; the
/// service layer enforces the 0–100 per-type bound (Req 5.4). Mutable fields advance
/// <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class CustomFieldDefinition : SystemEntity
{
    private string _displayName = string.Empty;
    private bool _isRequired;
    private int _sortOrder;
    private string? _optionsJson;

    /// <summary>Identity of the owning <see cref="BusinessTemplate"/>. Fixed at creation.</summary>
    public Guid TemplateId { get; init; }

    /// <summary>The single entity type this field is attached to (Req 5.4). Fixed at creation.</summary>
    public BusinessEntityType TargetEntityType { get; init; }

    /// <summary>The data type of the field value. Fixed at creation.</summary>
    public CustomFieldDataType DataType { get; init; }

    /// <summary>Stable key under which the field's value is stored in the target entity's <c>CustomFieldsJson</c>. Fixed at creation.</summary>
    public string FieldKey { get; init; } = string.Empty;

    /// <summary>User-visible display name of the field.</summary>
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; MarkUpdated(); }
    }

    /// <summary>Whether the field is required.</summary>
    public bool IsRequired
    {
        get => _isRequired;
        set { _isRequired = value; MarkUpdated(); }
    }

    /// <summary>Ordering hint for presenting the field.</summary>
    public int SortOrder
    {
        get => _sortOrder;
        set { _sortOrder = value; MarkUpdated(); }
    }

    /// <summary>Optional JSON list of allowed options for single/multi-select fields.</summary>
    public string? OptionsJson
    {
        get => _optionsJson;
        set { _optionsJson = value; MarkUpdated(); }
    }
}
