using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orderly.Core.Commerce.Connectors;

/// <summary>
/// Reserved, neutral connector specialization for exchanging inventory data with an external system
/// (Req 8.3). Declared but not wired to any active runtime implementation in V1.
/// <para>
/// In V1 a disabled connector returns <see cref="ConnectorOutcome.ConnectorDisabled"/> for every
/// operation, performing no outbound request and leaving all local data unchanged (Req 8.5).
/// </para>
/// </summary>
public interface IExternalInventoryConnector : IExternalConnector
{
    /// <summary>
    /// Pushes a batch of inventory records to the external system. Disabled in V1: returns a
    /// <see cref="ConnectorOutcome.ConnectorDisabled"/> result with no outbound request.
    /// </summary>
    /// <param name="records">The neutral inventory records to push.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<ConnectorResult> PushInventoryAsync(
        IReadOnlyList<ConnectorRecord> records,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls inventory records from the external system. Disabled in V1: returns a
    /// <see cref="ConnectorOutcome.ConnectorDisabled"/> result with no payload and no outbound request.
    /// </summary>
    /// <param name="query">The neutral pull query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<ConnectorResult<IReadOnlyList<ConnectorRecord>>> PullInventoryAsync(
        ConnectorQuery query,
        CancellationToken cancellationToken = default);
}
