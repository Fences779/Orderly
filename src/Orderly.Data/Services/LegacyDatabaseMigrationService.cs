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
            Directory.CreateDirectory(targetDirectory);
        }

        var targetExists = File.Exists(plan.TargetDatabasePath);
        if (targetExists && !overwriteTarget)
        {
            throw new InvalidOperationException("Target account database already exists. Explicit overwrite is required.");
        }

        File.Copy(plan.LegacyDatabasePath, plan.TargetDatabasePath, overwriteTarget);

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
}
