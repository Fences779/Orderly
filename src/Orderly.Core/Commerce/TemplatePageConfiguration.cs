namespace Orderly.Core.Commerce;

/// <summary>
/// The resolved show/hide state of a single page element (a metric card or a table column) within a
/// <see cref="BusinessTemplate"/>'s page configuration (Req 5.5). Each show/hide state resolves to
/// exactly one of the two values <see cref="Shown"/> or <see cref="Hidden"/> — there is no third state.
/// </summary>
public enum TemplateElementVisibility
{
    /// <summary>The element is shown.</summary>
    Shown = 0,

    /// <summary>The element is hidden.</summary>
    Hidden = 1
}

/// <summary>
/// The page configuration carried by a <see cref="BusinessTemplate"/> (Req 5.5). It captures the
/// metric-card show/hide state, the table-column show/hide state, and the page-level defaults — the
/// default sort, default unit, default currency, and default order flow.
///
/// <para>Each entry of <see cref="MetricCards"/> and <see cref="TableColumns"/> maps an element's
/// stable key to exactly one <see cref="TemplateElementVisibility"/> value, so every configured
/// show/hide state resolves to exactly <see cref="TemplateElementVisibility.Shown"/> or
/// <see cref="TemplateElementVisibility.Hidden"/> (Req 5.5). An element whose key is absent from the
/// map is treated as <see cref="TemplateElementVisibility.Shown"/> by <see cref="IsMetricCardShown"/>
/// / <see cref="IsTableColumnShown"/>, so an unconfigured element still resolves to a single defined
/// value.</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term.</para>
/// </summary>
public sealed record TemplatePageConfiguration
{
    /// <summary>
    /// Show/hide state per metric-card key (Req 5.5). A key absent from the map resolves to
    /// <see cref="TemplateElementVisibility.Shown"/>.
    /// </summary>
    public IReadOnlyDictionary<string, TemplateElementVisibility> MetricCards { get; init; }
        = new Dictionary<string, TemplateElementVisibility>();

    /// <summary>
    /// Show/hide state per table-column key (Req 5.5). A key absent from the map resolves to
    /// <see cref="TemplateElementVisibility.Shown"/>.
    /// </summary>
    public IReadOnlyDictionary<string, TemplateElementVisibility> TableColumns { get; init; }
        = new Dictionary<string, TemplateElementVisibility>();

    /// <summary>The default sort key applied to list pages, or <c>null</c> when no default is configured (Req 5.5).</summary>
    public string? DefaultSort { get; init; }

    /// <summary>The default unit-of-measure key, or <c>null</c> when no default is configured (Req 5.5).</summary>
    public string? DefaultUnit { get; init; }

    /// <summary>The default currency code, or <c>null</c> when no default is configured (Req 5.5).</summary>
    public string? DefaultCurrency { get; init; }

    /// <summary>The default order flow key, or <c>null</c> when no default is configured (Req 5.5).</summary>
    public string? DefaultOrderFlow { get; init; }

    /// <summary>
    /// Resolves whether the metric card identified by <paramref name="key"/> is shown. An unconfigured
    /// key resolves to shown, so the result is always exactly one of shown/hidden (Req 5.5).
    /// </summary>
    public bool IsMetricCardShown(string key) => ResolveShown(MetricCards, key);

    /// <summary>
    /// Resolves whether the table column identified by <paramref name="key"/> is shown. An unconfigured
    /// key resolves to shown, so the result is always exactly one of shown/hidden (Req 5.5).
    /// </summary>
    public bool IsTableColumnShown(string key) => ResolveShown(TableColumns, key);

    private static bool ResolveShown(IReadOnlyDictionary<string, TemplateElementVisibility> map, string key)
        => !map.TryGetValue(key, out TemplateElementVisibility visibility)
            || visibility == TemplateElementVisibility.Shown;
}
