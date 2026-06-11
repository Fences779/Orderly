using Orderly.Core.Models;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class LegacyDatabaseMigrationService : ILegacyDatabaseMigrationService
{
    public Task<LegacyDatabaseMigrationPlan> BuildPlanAsync(string ownerAccountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = Path.GetFullPath(DatabasePaths.GetLegacyDatabasePath());
        var targetPath = Path.GetFullPath(DatabasePaths.GetAccountDatabasePath(ownerAccountId));

        var sourceExists = File.Exists(sourcePath);
        var targetExists = File.Exists(targetPath);
        var sourceInfo = sourceExists ? new FileInfo(sourcePath) : null;
        var targetInfo = targetExists ? new FileInfo(targetPath) : null;

        var state = ResolveState(sourceExists, targetExists, sourcePath, targetPath);
        var message = state switch
        {
            LegacyDatabaseMigrationState.LegacyDatabaseMissing => "未发现 legacy orderly.db，跳过迁移。",
            LegacyDatabaseMigrationState.ReadyToCopy => "检测到 legacy orderly.db，可复制到 Owner 账号库。",
            LegacyDatabaseMigrationState.TargetAlreadyExists => "Owner 账号库已存在，若要导入 legacy 数据需显式覆盖。",
            LegacyDatabaseMigrationState.SourceAndTargetAreSameFile => "源库与目标库是同一路径，无需迁移。",
            _ => "未知迁移状态。"
        };

        LegacyDatabaseMigrationPlan plan = new()
        {
            State = state,
            LegacyDatabasePath = sourcePath,
            TargetDatabasePath = targetPath,
            LegacyDatabaseExists = sourceExists,
            TargetDatabaseExists = targetExists,
            LegacyDatabaseSizeBytes = sourceInfo?.Length ?? 0,
            TargetDatabaseSizeBytes = targetInfo?.Length ?? 0,
            Message = message
        };

        return Task.FromResult(plan);
    }

    public Task<LegacyDatabaseMigrationResult> CopyAsync(
        LegacyDatabaseMigrationPlan plan,
        bool overwriteTarget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!plan.LegacyDatabaseExists || !File.Exists(plan.LegacyDatabasePath))
        {
            throw new FileNotFoundException("Legacy database not found.", plan.LegacyDatabasePath);
        }

        if (LocalDataFileSecurity.IsReparsePoint(plan.LegacyDatabasePath))
        {
            throw new InvalidOperationException("Legacy database source cannot be a linked file.");
        }

        var sourceDirectory = Path.GetDirectoryName(plan.LegacyDatabasePath);
        if (!string.IsNullOrWhiteSpace(sourceDirectory))
        {
            LocalDataFileSecurity.EnsureDirectoryIsNotLinked(sourceDirectory, "Legacy 迁移源目录");
        }

        if (plan.State == LegacyDatabaseMigrationState.SourceAndTargetAreSameFile)
        {
            return Task.FromResult(new LegacyDatabaseMigrationResult
            {
                Plan = plan,
                Copied = false,
                Overwritten = false,
                ExecutedAt = DateTimeOffset.Now
            });
        }

        var targetDirectory = Path.GetDirectoryName(plan.TargetDatabasePath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(targetDirectory, "Legacy 迁移目标目录");
        }

        var targetExists = File.Exists(plan.TargetDatabasePath);
        if (targetExists && !overwriteTarget)
        {
            throw new InvalidOperationException("Target account database already exists. Explicit overwrite is required.");
        }

        if (LocalDataFileSecurity.IsReparsePoint(plan.TargetDatabasePath))
        {
            throw new InvalidOperationException("Target account database cannot be a linked file.");
        }

        CopyDatabaseAtomically(plan.LegacyDatabasePath, plan.TargetDatabasePath, overwriteTarget);
        LocalDataFileSecurity.HardenSqliteDatabaseFiles(plan.TargetDatabasePath);

        return Task.FromResult(new LegacyDatabaseMigrationResult
        {
            Plan = plan,
            Copied = true,
            Overwritten = targetExists && overwriteTarget,
            ExecutedAt = DateTimeOffset.Now
        });
    }

    private static LegacyDatabaseMigrationState ResolveState(
        bool sourceExists,
        bool targetExists,
        string sourcePath,
        string targetPath)
    {
        if (!sourceExists)
        {
            return LegacyDatabaseMigrationState.LegacyDatabaseMissing;
        }

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return LegacyDatabaseMigrationState.SourceAndTargetAreSameFile;
        }

        if (targetExists)
        {
            return LegacyDatabaseMigrationState.TargetAlreadyExists;
        }

        return LegacyDatabaseMigrationState.ReadyToCopy;
    }

    private static void CopyDatabaseAtomically(string sourcePath, string targetPath, bool overwriteTarget)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            targetDirectory = Directory.GetCurrentDirectory();
        }

        var tempPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            EnsureDatabaseFileIsNotLinked(sourcePath, "Legacy database source cannot be a linked file.");
            EnsureDatabaseFileIsNotLinked(tempPath, "Temporary account database cannot be a linked file.");
            CopyDatabaseToTemporaryFile(sourcePath, tempPath);
            EnsureDatabaseFileIsNotLinked(targetPath, "Target account database cannot be a linked file.");
            EnsureSqliteSidecarsAreNotLinked(targetPath);
            File.Move(tempPath, targetPath, overwrite: overwriteTarget);
            EnsureDatabaseFileIsNotLinked(targetPath, "Target account database cannot be a linked file.");
            DeleteSqliteSidecars(targetPath);
        }
        catch
        {
            DeleteTemporaryDatabaseFile(tempPath);
            throw;
        }
    }

    private static void CopyDatabaseToTemporaryFile(string sourcePath, string tempPath)
    {
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        if (LocalDataFileSecurity.IsReparsePoint(sourcePath))
        {
            throw new InvalidOperationException("Legacy database source cannot be a linked file.");
        }

        using var target = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.WriteThrough);
        source.CopyTo(target);
        target.Flush(flushToDisk: true);
    }

    private static void EnsureDatabaseFileIsNotLinked(string path, string message)
    {
        if (LocalDataFileSecurity.IsReparsePoint(path))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void EnsureSqliteSidecarsAreNotLinked(string databasePath)
    {
        foreach (var path in GetSqliteSidecarPaths(databasePath))
        {
            EnsureDatabaseFileIsNotLinked(path, "Target account database sidecar cannot be a linked file.");
        }
    }

    private static void DeleteSqliteSidecars(string databasePath)
    {
        foreach (var path in GetSqliteSidecarPaths(databasePath))
        {
            if (File.Exists(path))
            {
                EnsureDatabaseFileIsNotLinked(path, "Target account database sidecar cannot be a linked file.");
                File.Delete(path);
            }
        }
    }

    private static IEnumerable<string> GetSqliteSidecarPaths(string databasePath)
    {
        yield return databasePath + "-journal";
        yield return databasePath + "-wal";
        yield return databasePath + "-shm";
    }

    private static void DeleteTemporaryDatabaseFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath) && !LocalDataFileSecurity.IsReparsePoint(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
