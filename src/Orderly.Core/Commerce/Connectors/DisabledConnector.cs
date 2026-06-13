using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orderly.Core.Commerce.Connectors;

/// <summary>
/// Inert base implementation for a reserved, disabled connector (Req 8.3, 8.4, 8.5, 8.6).
/// <para>
/// This type performs NO I/O and NO outbound request. It always reports
/// <see cref="ConnectorHealthStatus.Disabled"/> health (Req 8.6) and is never enabled in V1
/// (Req 8.4). It exists only to give the disabled-connector behavior a concrete, testable shape
/// while keeping zero active runtime wiring.
/// </para>
/// </summary>
public abstract class DisabledConnectorBase : IExternalConnector
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public ConnectorOptions Options => ConnectorOptions.Disabled;

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public ConnectorHealthStatus Health => ConnectorHealthStatus.Disabled;
}

/// <summary>
/// A reserved, inert order connector that is disabled in V1 (Req 8.3, 8.4, 8.5, 8.6).
/// Every operation returns <see cref="ConnectorOutcome.ConnectorDisabled"/> without performing any
/// outbound request, leaving all local data unchanged.
/// </summary>
public sealed class DisabledOrderConnector : DisabledConnectorBase, IExternalOrderConnector
{
    /// <inheritdoc />
    public override string Name { get; }

    /// <summary>Creates a disabled order connector with an optional neutral name.</summary>
    /// <param name="name">A neutral connector name; defaults to <c>"OrderConnector"</c>.</param>
    public DisabledOrderConnector(string name = "OrderConnector") => Name = name;

    /// <inheritdoc />
    public Task<ConnectorResult> PushOrdersAsync(
        IReadOnlyList<ConnectorRecord> records,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ConnectorResult.Disabled());

    /// <inheritdoc />
    public Task<ConnectorResult<IReadOnlyList<ConnectorRecord>>> PullOrdersAsync(
        ConnectorQuery query,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ConnectorResult<IReadOnlyList<ConnectorRecord>>.Disabled());
}

/// <summary>
/// A reserved, inert inventory connector that is disabled in V1 (Req 8.3, 8.4, 8.5, 8.6).
/// Every operation returns <see cref="ConnectorOutcome.ConnectorDisabled"/> without performing any
/// outbound request, leaving all local data unchanged.
/// </summary>
public sealed class DisabledInventoryConnector : DisabledConnectorBase, IExternalInventoryConnector
{
    /// <inheritdoc />
    public override string Name { get; }

    /// <summary>Creates a disabled inventory connector with an optional neutral name.</summary>
    /// <param name="name">A neutral connector name; defaults to <c>"InventoryConnector"</c>.</param>
    public DisabledInventoryConnector(string name = "InventoryConnector") => Name = name;

    /// <inheritdoc />
    public Task<ConnectorResult> PushInventoryAsync(
        IReadOnlyList<ConnectorRecord> records,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ConnectorResult.Disabled());

    /// <inheritdoc />
    public Task<ConnectorResult<IReadOnlyList<ConnectorRecord>>> PullInventoryAsync(
        ConnectorQuery query,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ConnectorResult<IReadOnlyList<ConnectorRecord>>.Disabled());
}
