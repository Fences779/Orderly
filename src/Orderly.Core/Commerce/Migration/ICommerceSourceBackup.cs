namespace Orderly.Core.Commerce.Migration;

/// <summary>
/// Creates a complete backup of the legacy source database <b>before</b> a migration applies any
/// change (Requirement 3.8). The migration routine calls this first; if it reports failure the
/// routine aborts with <c>BackupFailedMigrationAborted</c> and leaves the source unmodified.
///
/// <para>The abstraction exists so the backup-first contract is independently testable: a test can
/// supply a backup that deterministically fails to assert the abort path, while the default
/// implementation performs a real file copy of the source database and its sidecar files.</para>
/// </summary>
public interface ICommerceSourceBackup
{
    /// <summary>
    /// Attempts to create a complete backup of the database at <paramref name="sourceDatabasePath"/>.
    /// Implementations MUST NOT modify the source. Returns a <see cref="CommerceSourceBackupResult"/>
    /// describing success (with the backup path) or failure (with a reason); implementations should
    /// not throw for an expected backup failure.
    /// </summary>
    Task<CommerceSourceBackupResult> CreateBackupAsync(string sourceDatabasePath, CancellationToken cancellationToken = default);
}

/// <summary>The result of a pre-migration source backup attempt (Req 3.8).</summary>
public sealed class CommerceSourceBackupResult
{
    private CommerceSourceBackupResult(bool succeeded, string? backupPath, string reason)
    {
        Succeeded = succeeded;
        BackupPath = backupPath;
        Reason = reason;
    }

    /// <summary>Whether the backup was created successfully.</summary>
    public bool Succeeded { get; }

    /// <summary>The absolute path of the created backup, or <c>null</c> on failure.</summary>
    public string? BackupPath { get; }

    /// <summary>A human-readable reason, used to explain a failure (Req 3.8).</summary>
    public string Reason { get; }

    /// <summary>Creates a successful result with the backup path.</summary>
    public static CommerceSourceBackupResult Success(string backupPath)
        => new(true, backupPath, "源数据库备份已创建。"); // "Source database backup created."

    /// <summary>Creates a failed result with the reason the backup could not be created.</summary>
    public static CommerceSourceBackupResult Failure(string reason)
        => new(false, null, reason);
}
