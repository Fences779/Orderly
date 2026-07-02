namespace Orderly.Contracts.Commerce;

public sealed class CloudCustomerDto : CloudEntityDto
{
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? WeChat { get; set; }
    public string? Email { get; set; }
    public DateTime? LastOrderAtUtc { get; set; }
    public int CompletedOrderCount { get; set; }
    public decimal TotalSpend { get; set; }
    public Guid? AssignedToUserId { get; set; }
}
