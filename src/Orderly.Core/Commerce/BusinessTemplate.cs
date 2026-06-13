namespace Orderly.Core.Commerce;

/// <summary>
/// A business template that customizes custom fields, page configuration, and workflow
/// configuration (Req 5.1, 5.5, 5.6). A template is either <b>built-in/system</b> — the single
/// built-in template with key <c>DefaultCommerce</c> and a null <see cref="WorkspaceId"/> — or
/// <b>workspace-owned</b> — a user-created/cloned template whose <see cref="WorkspaceId"/> is set
/// to its owning workspace (Req 5.3). It extends <see cref="SystemEntity"/> and carries its own
/// nullable <see cref="WorkspaceId"/> directly, so the field is null exactly when the template is
/// built-in. Mutable fields advance <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class BusinessTemplate : SystemEntity
{
    private string _displayName = string.Empty;
    private string? _configJson;

    /// <summary>
    /// Stable internal key (for example <c>DefaultCommerce</c> for the single built-in template).
    /// Fixed at creation.
    /// </summary>
    public string TemplateKey { get; init; } = string.Empty;

    /// <summary>
    /// Owning workspace identity, or null when the template is the built-in/system template
    /// (Req 5.3). Fixed at creation: null = built-in, set = workspace-owned.
    /// </summary>
    public Guid? WorkspaceId { get; init; }

    /// <summary><c>true</c> when this is a built-in/system template (and <see cref="WorkspaceId"/> is null).</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>User-visible display name (for the built-in template this is <c>默认经营模板</c>) (Req 5.8).</summary>
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; MarkUpdated(); }
    }

    /// <summary>
    /// JSON payload holding page configuration and workflow configuration for the template
    /// (Req 5.1, 5.5, 5.6). Stored as provided; well-formedness is enforced at the service boundary.
    /// </summary>
    public string? ConfigJson
    {
        get => _configJson;
        set { _configJson = value; MarkUpdated(); }
    }
}
