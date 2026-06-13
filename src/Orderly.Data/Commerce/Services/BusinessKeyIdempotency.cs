using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Shared idempotent-by-Business_Key persistence helper for the Commerce Service Layer (Req 4.20,
/// 18.6). Before a core write operation generates a <see cref="PaymentRecord"/>,
/// <see cref="CashFlowEntry"/>, <see cref="InventoryMovement"/>, <see cref="BusinessInsight"/>, or
/// <see cref="BusinessMetricSnapshot"/>, it routes the candidate record through this helper so that a
/// record carrying a Business_Key that already exists is linked/reused rather than inserted again.
/// Re-running the same completion or payment therefore produces no duplicate financial, inventory, or
/// insight records.
///
/// <para>The Business_Key is read through a caller-supplied selector because each entity exposes its
/// own <c>BusinessKey</c> property rather than a shared interface member. A candidate whose
/// Business_Key is null or empty opts out of idempotency and is always created — this preserves the
/// behavior of callers that do not assign a Business_Key (for example ad-hoc payments).</para>
///
/// <para>This helper reads/writes only through the Commerce repositories, so a write that runs inside
/// an ambient <c>Core_Write_Transaction</c> enrolls in that transaction (Req 18.1) and the
/// P0_Security_System (C-2) is unaffected.</para>
/// </summary>
internal static class BusinessKeyIdempotency
{
    /// <summary>
    /// Returns the existing active record whose Business_Key equals <paramref name="businessKey"/>,
    /// or <c>null</c> when <paramref name="businessKey"/> is null/empty or no record matches. Matching
    /// is an ordinal comparison of the value produced by <paramref name="businessKeySelector"/>.
    /// </summary>
    public static async Task<TEntity?> FindByBusinessKeyAsync<TEntity>(
        ICommerceRepository<TEntity> repository,
        string? businessKey,
        Func<TEntity, string?> businessKeySelector,
        CancellationToken cancellationToken = default)
        where TEntity : CommerceEntity
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(businessKeySelector);

        if (string.IsNullOrEmpty(businessKey))
        {
            return null;
        }

        IReadOnlyList<TEntity> all = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (TEntity record in all)
        {
            if (string.Equals(businessKeySelector(record), businessKey, StringComparison.Ordinal))
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>
    /// Idempotently persists <paramref name="candidate"/> by its Business_Key (Req 4.20, 18.6): when
    /// the candidate carries a non-empty Business_Key and an active record with the same key already
    /// exists, that existing record is returned and no duplicate is inserted; otherwise the candidate
    /// is created and returned. A candidate with no Business_Key is always created.
    /// </summary>
    public static async Task<TEntity> CreateIdempotentAsync<TEntity>(
        ICommerceRepository<TEntity> repository,
        TEntity candidate,
        Func<TEntity, string?> businessKeySelector,
        CancellationToken cancellationToken = default)
        where TEntity : CommerceEntity
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(businessKeySelector);

        string? businessKey = businessKeySelector(candidate);

        TEntity? existing = await FindByBusinessKeyAsync(
            repository,
            businessKey,
            businessKeySelector,
            cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await repository.CreateAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex) && !string.IsNullOrEmpty(businessKey))
        {
            // A concurrent core write inserted the same keyed record between our find and insert and
            // tripped the partial UNIQUE index (Req 4.20 / 18.6). Re-resolve the now-persisted record
            // and return it so the conflict surfaces as an idempotent success rather than an error.
            TEntity? raced = await FindByBusinessKeyAsync(
                repository,
                businessKey,
                businessKeySelector,
                cancellationToken).ConfigureAwait(false);

            return raced ?? throw new InvalidOperationException(
                "Business_Key 唯一约束冲突，但未能解析到已存在记录。", ex);
        }
    }

    /// <summary>
    /// Recognizes a SQLite UNIQUE-constraint violation (primary code <c>SQLITE_CONSTRAINT</c> = 19,
    /// extended code <c>SQLITE_CONSTRAINT_UNIQUE</c> = 2067) raised by the partial Business_Key index.
    /// </summary>
    private static bool IsUniqueConstraintViolation(SqliteException ex)
        => ex.SqliteErrorCode == 19 || ex.SqliteExtendedErrorCode == 2067;
}
