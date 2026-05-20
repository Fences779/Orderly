using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface ILegacyDatabaseMigrationService
{
    Task<LegacyDatabaseMigrationPlan> BuildPlanAsync(string ownerAccountId, CancellationToken cancellationToken = default);
    Task<LegacyDatabaseMigrationResult> CopyAsync(
        LegacyDatabaseMigrationPlan plan,
        bool overwriteTarget,
        CancellationToken cancellationToken = default);
}
