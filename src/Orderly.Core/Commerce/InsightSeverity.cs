namespace Orderly.Core.Commerce;

/// <summary>
/// The severity of a generated business insight (<c>BusinessInsight</c>).
/// </summary>
public enum InsightSeverity
{
    /// <summary>Informational; no action required.</summary>
    Info = 0,

    /// <summary>A condition that warrants attention.</summary>
    Warning = 1,

    /// <summary>A condition that requires prompt action.</summary>
    Critical = 2
}
