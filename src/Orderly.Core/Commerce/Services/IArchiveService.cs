namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Lists archived Commerce entities and recovers them. Only available when connected to
/// Orderly.Server; local-only deployments may provide a no-op implementation.
/// </summary>
public interface IArchiveService
{
    /// <summary>Lists archived entities of the requested type (e.g. "orders", "customers").</summary>
    Task<IReadOnlyList<ArchivedEntitySummary>> ListAsync(
        string entityType,
        CancellationToken cancellationToken = default);

    /// <summary>Recovers an archived entity.</summary>
    Task RecoverAsync(
        string entityType,
        Guid entityId,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}

/// <summary>Lightweight summary of an archived entity for the archive-data UI.</summary>
public sealed record ArchivedEntitySummary(
    Guid Id,
    string EntityType,
    string Name,
    DateTime? ArchivedAtUtc,
    string? ArchivedByDisplayName,
    string? ArchiveReason,
    long Revision);
