namespace Orderly.Core.Models;

public enum BackupRestoreTargetState
{
    Unknown = 0,
    EmptyDatabase = 1,
    QaDatabase = 2,
    NonEmptyProductionDatabase = 3
}
