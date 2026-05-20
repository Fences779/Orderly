namespace Orderly.Core.Models;

public sealed class LegacyDatabaseMigrationPlan
{
    public LegacyDatabaseMigrationState State { get; init; }
    public string LegacyDatabasePath { get; init; } = string.Empty;
    public string TargetDatabasePath { get; init; } = string.Empty;
    public bool LegacyDatabaseExists { get; init; }
    public bool TargetDatabaseExists { get; init; }
    public long LegacyDatabaseSizeBytes { get; init; }
    public long TargetDatabaseSizeBytes { get; init; }
    public string Message { get; init; } = string.Empty;

    public bool CanCopyWithoutOverwrite => State == LegacyDatabaseMigrationState.ReadyToCopy;
    public bool RequiresExplicitOverwrite => State == LegacyDatabaseMigrationState.TargetAlreadyExists;
}
