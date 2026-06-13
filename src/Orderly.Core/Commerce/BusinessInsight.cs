namespace Orderly.Core.Commerce;

/// <summary>
/// A generated business insight owned by a single workspace (Req 2.2). Insights are produced by
/// deterministic local rules (Req 4.14). The optional <see cref="BusinessKey"/> makes generation
/// idempotent so re-running insight generation produces no duplicates (Req 4.20, 18.6). Mutable
/// fields advance <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class BusinessInsight : WorkspaceScopedEntity
{
    private InsightSeverity _severity = InsightSeverity.Info;
    private string _title = string.Empty;
    private string _message = string.Empty;
    private string? _category;
    private bool _isAcknowledged;

    /// <summary>Severity of the insight.</summary>
    public InsightSeverity Severity
    {
        get => _severity;
        set { _severity = value; MarkUpdated(); }
    }

    /// <summary>Short title of the insight.</summary>
    public string Title
    {
        get => _title;
        set { _title = value; MarkUpdated(); }
    }

    /// <summary>Human-readable insight message.</summary>
    public string Message
    {
        get => _message;
        set { _message = value; MarkUpdated(); }
    }

    /// <summary>Optional category label grouping the insight.</summary>
    public string? Category
    {
        get => _category;
        set { _category = value; MarkUpdated(); }
    }

    /// <summary>Whether the user has acknowledged the insight.</summary>
    public bool IsAcknowledged
    {
        get => _isAcknowledged;
        set { _isAcknowledged = value; MarkUpdated(); }
    }

    /// <summary>The UTC moment the insight was generated. Fixed at creation.</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>Stable business key used for idempotent generation by the service layer (Req 4.20, 18.6).</summary>
    public string? BusinessKey { get; init; }
}
