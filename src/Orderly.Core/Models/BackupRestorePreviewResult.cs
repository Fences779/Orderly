namespace Orderly.Core.Models;

public sealed class BackupRestorePreviewResult
{
    public string BackupPath { get; set; } = string.Empty;
    public BackupValidationResult Validation { get; set; } = new();
    public BackupRestoreTargetState TargetState { get; set; } = BackupRestoreTargetState.Unknown;
    public IReadOnlyDictionary<string, int> TargetCounts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public bool RequiresQaDataClear { get; set; }
    public bool IsQaTaggedBackup { get; set; }
    public bool CanRestore { get; set; }
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<string> Errors { get; set; } = [];
}
