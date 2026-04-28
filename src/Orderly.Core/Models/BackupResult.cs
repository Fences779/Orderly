namespace Orderly.Core.Models;

public sealed class BackupResult
{
    public int SyncRecordId { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    public string BackupPath { get; set; } = string.Empty;
    public string ErrorSummary { get; set; } = string.Empty;
    public BackupManifest Manifest { get; set; } = new();
    public BackupRestoreTargetState TargetState { get; set; } = BackupRestoreTargetState.Unknown;
    public bool QaDataCleared { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
