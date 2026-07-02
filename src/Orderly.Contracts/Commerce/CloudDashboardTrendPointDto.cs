namespace Orderly.Contracts.Commerce;

public sealed class CloudDashboardTrendPointDto
{
    public DateTime DateUtc { get; set; }
    public int CompletedOrderCount { get; set; }
    public decimal Revenue { get; set; }
    public decimal? CashInflow { get; set; }
    public decimal? CashOutflow { get; set; }
}
