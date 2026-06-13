namespace Orderly.Core.Commerce;

/// <summary>
/// Defines a unit of measure (Req 2.2). A unit is either <b>built-in/system</b> (a null
/// <see cref="TemplateId"/>) or <b>template-scoped</b> (a user-defined unit owned by one template).
/// It extends <see cref="SystemEntity"/> and carries no <c>WorkspaceId</c>; workspace ownership is
/// transitive through the owning template. Mutable fields advance
/// <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class UnitDefinition : SystemEntity
{
    private string _displayName = string.Empty;

    /// <summary>Owning <see cref="BusinessTemplate"/> for user-defined units, or null for built-in units. Fixed at creation.</summary>
    public Guid? TemplateId { get; init; }

    /// <summary>Stable code for the unit (for example a short symbol). Fixed at creation.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary><c>true</c> when this is a built-in/system unit (and <see cref="TemplateId"/> is null).</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>User-visible display name of the unit.</summary>
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; MarkUpdated(); }
    }
}
