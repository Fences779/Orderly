namespace Orderly.Core.Commerce.Connectors;

/// <summary>
/// The health status of a reserved <see cref="IExternalConnector"/> (Req 8.6).
/// <para>
/// In V1 every defined connector is reserved and not wired to any active runtime
/// implementation (Req 8.3); each therefore reports <see cref="Disabled"/> health at
/// startup (Req 8.6). <see cref="Disabled"/> is intentionally the default value (0) so an
/// uninitialised connector reads as disabled.
/// </para>
/// </summary>
public enum ConnectorHealthStatus
{
    /// <summary>
    /// The connector is reserved and turned off; it performs no outbound request and exposes
    /// no enabled configuration entry point. This is the default and the startup value in V1.
    /// </summary>
    Disabled = 0,

    /// <summary>The connector is enabled and fully operational. Not used in V1.</summary>
    Healthy = 1,

    /// <summary>The connector is enabled but operating with reduced capability. Not used in V1.</summary>
    Degraded = 2,

    /// <summary>The connector is enabled but currently unreachable. Not used in V1.</summary>
    Unavailable = 3
}
