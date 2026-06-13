namespace Orderly.Core.Commerce.Connectors;

/// <summary>
/// Neutral, industry-agnostic configuration options for a reserved <see cref="IExternalConnector"/>.
/// <para>
/// These options are declared but not wired to any active runtime implementation in V1 (Req 8.3).
/// <see cref="Enabled"/> defaults to <c>false</c>, so every connector is disabled by default
/// (Req 8.4); the placeholder fields exist only to describe a future connector and carry no
/// active behavior.
/// </para>
/// </summary>
public sealed record ConnectorOptions
{
    /// <summary>
    /// Whether the connector is enabled. Defaults to <c>false</c> in V1; a disabled connector
    /// performs no outbound request and exposes no enabled configuration entry point (Req 8.4).
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>An optional neutral display name placeholder for the connector. No active wiring.</summary>
    public string? Name { get; init; }

    /// <summary>An optional neutral outbound endpoint placeholder. No active wiring in V1.</summary>
    public string? Endpoint { get; init; }

    /// <summary>The canonical disabled options instance used by reserved connectors in V1.</summary>
    public static ConnectorOptions Disabled { get; } = new() { Enabled = false };
}
