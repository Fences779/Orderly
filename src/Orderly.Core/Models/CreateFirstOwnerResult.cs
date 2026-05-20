namespace Orderly.Core.Models;

public sealed class CreateFirstOwnerResult
{
    public LocalSessionContext Session { get; init; } = new();
    public string RecoveryKey { get; init; } = string.Empty;
    public LegacyDatabaseMigrationPlan LegacyMigrationPlan { get; init; } = new();
    public LegacyDatabaseMigrationResult? LegacyMigrationResult { get; init; }
}
