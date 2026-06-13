namespace Orderly.Core.Commerce.Connectors;

/// <summary>
/// The outcome of invoking a reserved <see cref="IExternalConnector"/> operation.
/// <para>
/// In V1 every connector is disabled, so every invocation returns
/// <see cref="ConnectorDisabled"/> without performing any outbound request (Req 8.5).
/// <see cref="ConnectorDisabled"/> is the default value (0).
/// </para>
/// </summary>
public enum ConnectorOutcome
{
    /// <summary>
    /// The connector is disabled. No outbound request was performed and all local data is
    /// preserved unchanged. This is the only outcome a connector can return in V1 (Req 8.5).
    /// </summary>
    ConnectorDisabled = 0,

    /// <summary>The operation completed successfully. Not produced in V1.</summary>
    Succeeded = 1,

    /// <summary>The operation was attempted but failed. Not produced in V1.</summary>
    Failed = 2
}

/// <summary>
/// A neutral, industry-agnostic envelope for a single record exchanged with a connector.
/// The payload is carried as an opaque string (for example serialized JSON) so the reserved
/// connector contracts stay decoupled from the domain model and free of any industry-specific
/// shape. Reserved for future connector implementations; unused at runtime in V1.
/// </summary>
/// <param name="RecordKey">A stable, opaque key identifying the record.</param>
/// <param name="PayloadJson">An opaque serialized payload for the record.</param>
public sealed record ConnectorRecord(string RecordKey, string PayloadJson);

/// <summary>
/// A neutral query passed to a connector pull operation. Reserved for future use; ignored by
/// disabled connectors in V1.
/// </summary>
/// <param name="Filter">An optional opaque filter expression.</param>
/// <param name="MaxRecords">An optional maximum number of records to retrieve.</param>
public sealed record ConnectorQuery(string? Filter = null, int? MaxRecords = null);

/// <summary>
/// The result of a reserved connector operation that carries no data payload.
/// <para>
/// A disabled connector returns <see cref="ConnectorOutcome.ConnectorDisabled"/> for every
/// operation, having performed no outbound request and left all local data unchanged (Req 8.5).
/// </para>
/// </summary>
public sealed record ConnectorResult
{
    /// <summary>The outcome of the operation.</summary>
    public ConnectorOutcome Outcome { get; init; }

    /// <summary>An optional neutral, human-readable explanation of the outcome.</summary>
    public string? Message { get; init; }

    /// <summary>True when the operation was rejected because the connector is disabled.</summary>
    public bool IsDisabled => Outcome == ConnectorOutcome.ConnectorDisabled;

    /// <summary>
    /// Creates the canonical "connector disabled" result returned by every disabled connector
    /// invocation in V1.
    /// </summary>
    public static ConnectorResult Disabled(string? message = null) => new()
    {
        Outcome = ConnectorOutcome.ConnectorDisabled,
        Message = message ?? "The connector is disabled; no outbound request was performed and local data is unchanged."
    };
}

/// <summary>
/// The result of a reserved connector operation that carries a data payload.
/// <para>
/// A disabled connector returns <see cref="ConnectorOutcome.ConnectorDisabled"/> with a default
/// (absent) payload, having performed no outbound request and left all local data unchanged (Req 8.5).
/// </para>
/// </summary>
/// <typeparam name="TData">The type of the carried payload.</typeparam>
public sealed record ConnectorResult<TData>
{
    /// <summary>The outcome of the operation.</summary>
    public ConnectorOutcome Outcome { get; init; }

    /// <summary>An optional neutral, human-readable explanation of the outcome.</summary>
    public string? Message { get; init; }

    /// <summary>The data payload, present only when <see cref="Outcome"/> is <see cref="ConnectorOutcome.Succeeded"/>.</summary>
    public TData? Data { get; init; }

    /// <summary>True when the operation was rejected because the connector is disabled.</summary>
    public bool IsDisabled => Outcome == ConnectorOutcome.ConnectorDisabled;

    /// <summary>
    /// Creates the canonical "connector disabled" result (with no payload) returned by every
    /// disabled connector invocation in V1.
    /// </summary>
    public static ConnectorResult<TData> Disabled(string? message = null) => new()
    {
        Outcome = ConnectorOutcome.ConnectorDisabled,
        Message = message ?? "The connector is disabled; no outbound request was performed and local data is unchanged.",
        Data = default
    };
}
