using Orderly.Core.Commerce.Migration;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

/// <summary>
/// Default <see cref="ICommerceSourceBackup"/>: creates a complete, read-only copy of the legacy
/// source database file (and any SQLite sidecar files) before a migration applies any change
/// (Requirement 3.8). The copy is written into a dedicated backup directory and the source file is
/// never modified, satisfying the non-destructive guarantee (Req 3.7) for the backup step itself.
///
/// <para>If the source cannot be read or the backup cannot be written, the method returns a failed
/// <see cref="CommerceSourceBackupResult"/> with the reason rather than throwing, so the migration
/// routine can abort cleanly with <c>BackupFailedMigrationAborted</c> (Req 3.8).</para>
/// </summary>
public sealed class CommerceSourceFileBackup : ICommerceSourceBackup
{
    private readonly string? _backupDirectory;

    /// <summary>
    /// Creates a backup helper. When <paramref name="backupDirectory"/> is null the backup is
    /// written to a <c>migration-backups</c> folder next to the source database.
    /// </summary>
    public CommerceSourceFileBackup(string? backupDirectory = null)
    {
        _backupDirectory = backupDirectory;
    }

    private static readonly string[] SidecarSuffixes = { "-wal", "-shm", "-journal" };

    public Task<CommerceSourceBackupResult> CreateBackupAsync(string sourceDatabasePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (string.IsNullOrWhiteSpace(sourceDatabasePath) || !File.Exists(sourceDatabasePath))
            {
                return Task.FromResult(CommerceSourceBackupResult.Failure(
                    $"无法创建源数据库备份：源文件不存在（{sourceDatabasePath}）。"));
            }

            // Reject linked source/target files for the same reasons as the launcher migration path.
            LocalDataFileSecurity.EnsureFileIsNotLinked(sourceDatabasePath, "Legacy 迁移源数据库文件");

            string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceDatabasePath))
                ?? Directory.GetCurrentDirectory();
            string backupDirectory = _backupDirectory ?? Path.Combine(sourceDirectory, "migration-backups");
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(backupDirectory, "Legacy 迁移备份目录");

            string fileName = Path.GetFileName(sourceDatabasePath);
            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            string backupPath = Path.Combine(backupDirectory, $"{fileName}.{stamp}.bak");

            LocalDataFileSecurity.EnsureFileIsNotLinked(backupPath, "Legacy 迁移备份文件");
            File.Copy(sourceDatabasePath, backupPath, overwrite: false);

            // Copy any sidecar files so the backup is a complete point-in-time snapshot.
            foreach (string suffix in SidecarSuffixes)
            {
                string sidecar = sourceDatabasePath + suffix;
                if (File.Exists(sidecar) && !LocalDataFileSecurity.IsReparsePoint(sidecar))
                {
                    File.Copy(sidecar, backupPath + suffix, overwrite: false);
                }
            }

            LocalDataFileSecurity.HardenSqliteDatabaseFiles(backupPath);
            return Task.FromResult(CommerceSourceBackupResult.Success(backupPath));
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException
                or InvalidOperationException)
        {
            return Task.FromResult(CommerceSourceBackupResult.Failure(
                $"无法创建源数据库备份：{ex.Message}"));
        }
    }
}
