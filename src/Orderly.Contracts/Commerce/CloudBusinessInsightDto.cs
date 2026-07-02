using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class CloudBusinessInsightDto : CloudEntityDto
{
    public Guid WorkspaceId { get; set; }
    public InsightSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string? BusinessKey { get; set; }
}
