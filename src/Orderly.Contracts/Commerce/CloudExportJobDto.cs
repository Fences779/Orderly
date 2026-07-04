namespace Orderly.Contracts.Commerce;

public sealed class CloudExportJobDto : CloudEntityDto
{
    public Guid WorkspaceId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
