namespace Orderly.Core.Commerce;

/// <summary>
/// The scoping root of the Universal_Domain_Model: every workspace-scoped entity belongs to
/// exactly one workspace (Req 2.7). As the scoping root it extends <see cref="SystemEntity"/> and
/// carries no <c>WorkspaceId</c> (it <i>is</i> the workspace). A workspace with no explicitly
/// activated template resolves to the built-in <c>DefaultCommerce</c> template (Req 5.7). Mutable
/// fields advance <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class BusinessWorkspace : SystemEntity
{
    private string _name = string.Empty;
    private Guid? _activeTemplateId;
    private string? _defaultCurrencyCode;

    /// <summary>Display name of the workspace.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; MarkUpdated(); }
    }

    /// <summary>
    /// Identity of the explicitly activated <see cref="BusinessTemplate"/>, or null to fall back to
    /// the built-in <c>DefaultCommerce</c> template (Req 5.7).
    /// </summary>
    public Guid? ActiveTemplateId
    {
        get => _activeTemplateId;
        set { _activeTemplateId = value; MarkUpdated(); }
    }

    /// <summary>Optional default currency code for the workspace.</summary>
    public string? DefaultCurrencyCode
    {
        get => _defaultCurrencyCode;
        set { _defaultCurrencyCode = value; MarkUpdated(); }
    }
}
