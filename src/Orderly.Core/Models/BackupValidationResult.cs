namespace Orderly.Core.Models;

public sealed class BackupValidationResult
{
    public string BackupPath { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public BackupManifest? Manifest { get; set; }
    public string ActualChecksum { get; set; } = string.Empty;
    public IReadOnlyList<string> Errors { get; set; } = [];
}
