using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Migration;
using Orderly.Core.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;
/// <summary>
/// Wires the non-destructive, backup-first, idempotent legacy CRM migration
/// (<see cref="CommerceLegacyMigrationService"/>) into the application startup / workspace
/// initialization flow (Req 3.4–3.10). Without this runner the migration was implemented but never
/// invoked, so an upgrading user's legacy CRM rows stayed in the legacy tables while the new Commerce
/// pages — which read only the Commerce tables — appeared empty ("data disappeared").
///
/// <para><b>Same database, two schemas.</b> The legacy CRM tables and the Commerce tables live in the
/// same per-workspace encrypted database (created by <see cref="DatabaseInitializer"/>), so the same
/// <see cref="SqliteConnectionFactory"/> is used as both the migration source and target. The
/// SQLCipher key is applied on every connection exactly as elsewhere (C-2 unaffected).</para>
///
/// <para><b>Run-once across startups (idempotent).</b> The underlying migration is itself idempotent
/// (deterministic target ids + upsert), but re-reading/re-writing every row and taking a fresh source
/// backup on <i>every</i> launch would be wasteful. This runner therefore records completion in the
/// migration log and skips the work on subsequent launches once a <c>MigrationCompleted</c> entry
/// exists. A run that does not complete (e.g. the pre-migration backup failed, or the process was
/// interrupted) leaves no completion marker, so the next launch retries; the legacy source is never
/// modified, so a retry is always safe.</para>
///
/// <para><b>Stable target workspace.</b> Migrated records are owned by a fixed primary workspace whose
/// id is constant across runs so the deterministic target identities never change between launches
/// (a changing workspace id would defeat idempotence). The workspace row is created on first run if no
/// workspace with that id exists yet.</para>
/// </summary>
public sealed class CommerceStartupMigrationService
{
    /// <summary>
    /// The stable identity of the primary workspace that owns migrated legacy records. Fixed so the
    /// migration's deterministic target ids are identical across runs (idempotence, Req 3.6).
    /// </summary>
    public static readonly Guid PrimaryWorkspaceId = new("1f9e7c6a-0d3b-4a2e-9f51-6c4b8e2a7d10");

    /// <summary>Neutral display name of the primary workspace created on first migration.</summary>
    public const string PrimaryWorkspaceName = "默认工作区";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ICommerceSourceBackup? _backup;
    private readonly IFieldEncryptionService? _fieldEncryption;

    /// <summary>
    /// Creates the startup migration runner.
    /// </summary>
    /// <param name="connectionFactory">The per-workspace encrypted database (legacy source and Commerce target).</param>
    /// <param name="backup">
    /// Optional pre-migration backup strategy; defaults to a file-copy backup of the source database.
    /// </param>
    /// <param name="fieldEncryption">
    /// Optional field-encryption service so the migration can read legacy sensitive columns that have
    /// already been migrated to the P0 encrypted format (decrypting them rather than reading the
    /// cleared plaintext columns). Falls back to plaintext when null or unavailable.
    /// </param>
    public CommerceStartupMigrationService(
        SqliteConnectionFactory connectionFactory,
        ICommerceSourceBackup? backup = null,
        IFieldEncryptionService? fieldEncryption = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _backup = backup;
        _fieldEncryption = fieldEncryption;
    }

    /// <summary>
    /// Ensures the Commerce schema exists, runs the legacy migration once (idempotently across
    /// launches), and returns its result. On a fresh database with no legacy data the migration
    /// completes as a no-op so new users start normally. The legacy source is never modified, so a
    /// failed or interrupted run leaves all legacy data intact and is retried on the next launch.
    /// </summary>
    public async Task<CommerceLegacyMigrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        // Ensure the Commerce schema (and therefore the migration log + workspace tables) exists before
        // we consult the completion marker or write the primary workspace.
        await new CommerceSchemaInitializer(_connectionFactory).InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (await HasCompletedMigrationAsync(cancellationToken).ConfigureAwait(false))
        {
            return new CommerceLegacyMigrationResult
            {
                Outcome = CommerceLegacyMigrationOutcome.Completed,
                OutcomeToken = CommerceLegacyMigrationOutcomeTokens.MigrationCompleted,
                MigratedRecordCount = 0,
                Reason = "迁移此前已完成，跳过。", // "Migration already completed previously; skipped."
            };
        }

        await EnsurePrimaryWorkspaceAsync(cancellationToken).ConfigureAwait(false);

        var migration = new CommerceLegacyMigrationService(
            _connectionFactory,
            _connectionFactory,
            PrimaryWorkspaceId,
            _backup,
            _fieldEncryption);

        return await migration.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns true when a prior run recorded a <c>MigrationCompleted</c> outcome in the migration log,
    /// so the migration should not run again. Any read error (e.g. the log table is missing) is treated
    /// as "not completed" so the migration proceeds rather than being silently skipped.
    /// </summary>
    private async Task<bool> HasCompletedMigrationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT 1 FROM \"CommerceLegacyMigrationLog\" WHERE OutcomeToken = $token LIMIT 1;";
            command.Parameters.AddWithValue("$token", CommerceLegacyMigrationOutcomeTokens.MigrationCompleted);
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is not null;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the fixed primary workspace if it does not yet exist (idempotent). Migrated records are
    /// owned by this workspace.
    /// </summary>
    private async Task EnsurePrimaryWorkspaceAsync(CancellationToken cancellationToken)
    {
        var workspaces = new BusinessWorkspaceRepository(_connectionFactory);
        BusinessWorkspace? existing = await workspaces
            .GetByIdIncludingDeletedAsync(PrimaryWorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        await workspaces.CreateAsync(
            new BusinessWorkspace
            {
                Id = PrimaryWorkspaceId,
                CreatedAt = DateTime.UtcNow,
                Name = PrimaryWorkspaceName,
                DefaultCurrencyCode = "CNY",
            },
            cancellationToken).ConfigureAwait(false);
    }
}
