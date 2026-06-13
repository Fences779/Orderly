namespace Orderly.Core.Commerce.Migration;

/// <summary>
/// The terminal outcome of a non-destructive legacy CRM migration (Requirements 3.4–3.9).
/// The string form of each outcome is exposed as a stable token through
/// <see cref="CommerceLegacyMigrationOutcomeTokens"/> so callers, logs, and tests can match on a
/// deterministic, industry-agnostic identifier.
/// </summary>
public enum CommerceLegacyMigrationOutcome
{
    /// <summary>
    /// The migration ran to completion: the source backup was created first, every mapped legacy
    /// record was migrated non-destructively, and the run was idempotent (Req 3.6, 3.7, 3.9).
    /// </summary>
    Completed = 0,

    /// <summary>
    /// The required pre-migration backup of the source database could not be created, so the
    /// migration was aborted before applying any change and the source was left unmodified
    /// (Req 3.8). Surfaced through the <c>BackupFailedMigrationAborted</c> token.
    /// </summary>
    BackupFailedMigrationAborted = 1,

    /// <summary>
    /// No legacy source database was present, so there was nothing to migrate and no change was
    /// applied. A no-op success distinct from an actual data migration.
    /// </summary>
    SourceDatabaseMissing = 2
}

/// <summary>
/// Stable, industry-agnostic string tokens for each <see cref="CommerceLegacyMigrationOutcome"/>.
/// The <c>BackupFailedMigrationAborted</c> token is the contract value required by Requirement 3.8.
/// </summary>
public static class CommerceLegacyMigrationOutcomeTokens
{
    /// <summary>Token recorded when the migration ran to completion.</summary>
    public const string MigrationCompleted = "MigrationCompleted";

    /// <summary>Token recorded when the pre-migration source backup failed and the run was aborted (Req 3.8).</summary>
    public const string BackupFailedMigrationAborted = "BackupFailedMigrationAborted";

    /// <summary>Token recorded when no legacy source database existed and there was nothing to migrate.</summary>
    public const string SourceDatabaseMissing = "SourceDatabaseMissing";

    /// <summary>Maps an <see cref="CommerceLegacyMigrationOutcome"/> to its stable string token.</summary>
    public static string ToToken(CommerceLegacyMigrationOutcome outcome) => outcome switch
    {
        CommerceLegacyMigrationOutcome.Completed => MigrationCompleted,
        CommerceLegacyMigrationOutcome.BackupFailedMigrationAborted => BackupFailedMigrationAborted,
        CommerceLegacyMigrationOutcome.SourceDatabaseMissing => SourceDatabaseMissing,
        _ => outcome.ToString()
    };
}
