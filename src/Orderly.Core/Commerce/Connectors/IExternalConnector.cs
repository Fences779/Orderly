namespace Orderly.Core.Commerce.Connectors;

/// <summary>
/// Base abstraction for a reserved, neutral external connector (Req 8.3).
/// <para>
/// Connectors are an opt-in extension point for optional outbound integrations. In V1 they are
/// reserved and NOT wired to any active runtime implementation: every connector is disabled by
/// default (Req 8.4), reports <see cref="ConnectorHealthStatus.Disabled"/> health at startup
/// (Req 8.6), and rejects any invocation without performing an outbound request while preserving
/// all local data unchanged (Req 8.5).
/// </para>
/// </summary>
public interface IExternalConnector
{
    /// <summary>A neutral, industry-agnostic identifier for the connector.</summary>
    string Name { get; }

    /// <summary>The connector's configuration options. Disabled by default in V1 (Req 8.4).</summary>
    ConnectorOptions Options { get; }

    /// <summary>
    /// Whether the connector is enabled. Derived from <see cref="ConnectorOptions.Enabled"/>;
    /// always <c>false</c> in V1.
    /// </summary>
    bool IsEnabled => Options.Enabled;

    /// <summary>
    /// The current health status. Every reserved connector reports
    /// <see cref="ConnectorHealthStatus.Disabled"/> in V1 (Req 8.6).
    /// </summary>
    ConnectorHealthStatus Health { get; }
}
