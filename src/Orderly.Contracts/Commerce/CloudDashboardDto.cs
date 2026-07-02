namespace Orderly.Contracts.Commerce;

public sealed class CloudDashboardDto
{
    public DateTime AsOfUtc { get; set; }
    public int TotalOrders { get; set; }
    public int CompletedOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal? GrossProfit { get; set; }
    public decimal OutstandingReceivable { get; set; }
    public decimal? CashInflow { get; set; }
    public decimal? CashOutflow { get; set; }
    public decimal? NetCashFlow { get; set; }
    public int CustomerCount { get; set; }
    public int LowStockItemCount { get; set; }
    public List<CloudDashboardTrendPointDto> Trend { get; set; } = new();
    public long LatestSequence { get; set; }
}
