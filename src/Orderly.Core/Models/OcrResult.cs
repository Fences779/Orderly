namespace Orderly.Core.Models;

public sealed class OcrResult : EntityBase
{
    public int? CustomerId { get; set; }
    public int? OrderId { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string ExtractedText { get; set; } = string.Empty;
    public OcrStatus Status { get; set; } = OcrStatus.Pending;
    public string ErrorMessage { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;
}
