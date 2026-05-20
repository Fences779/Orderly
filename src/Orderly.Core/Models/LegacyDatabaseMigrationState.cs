namespace Orderly.Core.Models;

public enum LegacyDatabaseMigrationState
{
    LegacyDatabaseMissing = 0,
    ReadyToCopy = 1,
    TargetAlreadyExists = 2,
    SourceAndTargetAreSameFile = 3
}
