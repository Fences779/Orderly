namespace Orderly.Contracts.Commerce;

public sealed class CloudPriceChangeRequestDto : CloudEntityDto
{
    public Guid WorkspaceId { get; set; }
    public Guid ProductId { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal ProposedPrice { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid RequestedByUserId { get; set; }
    public string RequestedByDisplayName { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewedByDisplayName { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public long? AppliedProductRevision { get; set; }
}
