namespace Orderly.Core.Commerce.Migration;

/// <summary>
/// The result of a legacy CRM migration run. Carries the terminal <see cref="Outcome"/> and its
/// stable <see cref="OutcomeToken"/>, the count of migrated records (Req 3.9), a human-readable
/// <see cref="Reason"/>, the path of the pre-migration source backup when one was created
/// (Req 3.8), and a per-target breakdown of how many records each mapping produced.
///
/// <para>The object is the testable surface for the migration routine (Req 3.10): the property and
/// integration tests assert on <see cref="Outcome"/>, <see cref="MigratedRecordCount"/>, and
/// <see cref="CountsByTarget"/> to verify idempotence, non-destructiveness, and the criterion-4
/// mappings.</para>
/// </summary>
public sealed class CommerceLegacyMigrationResult
{
    /// <summary>The terminal outcome of the run.</summary>
    public required CommerceLegacyMigrationOutcome Outcome { get; init; }

    /// <summary>The stable string token for <see cref="Outcome"/> (e.g. <c>BackupFailedMigrationAborted</c>).</summary>
    public required string OutcomeToken { get; init; }

    /// <summary>Whether the migration ran to completion (created its backup and migrated all mapped records).</summary>
    public bool Succeeded => Outcome == CommerceLegacyMigrationOutcome.Completed;

    /// <summary>The total number of target records created or updated by the run (Req 3.9).</summary>
    public int MigratedRecordCount { get; init; }

    /// <summary>
    /// The absolute path of the complete source-database backup created before any change
    /// (Req 3.8), or <c>null</c> when no backup was created (e.g. the run was aborted because the
    /// backup failed, or there was no source database).
    /// </summary>
    public string? BackupPath { get; init; }

    /// <summary>A human-readable explanation of the outcome, including the reason a run was aborted.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The UTC moment the run finished.</summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// A breakdown of how many target records were produced per logical target
    /// (e.g. <c>Customer</c>, <c>Order</c>, <c>BusinessTask</c>, <c>note</c>). Useful for verifying
    /// the criterion-4 mappings (Req 3.4, 3.10).
    /// </summary>
    public IReadOnlyDictionary<string, int> CountsByTarget { get; init; }
        = new Dictionary<string, int>();
}
