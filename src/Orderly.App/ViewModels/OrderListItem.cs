using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public sealed class OrderListItem
{
    public OrderListItem(MerchantOrder order)
    {
        Order = order;
    }

    public MerchantOrder Order { get; }
    public int Id => Order.Id;
    public string RemoteId => Order.RemoteId;
    public string Title => Order.Title;
    public string TitleDisplay => string.IsNullOrWhiteSpace(Order.Title) ? "未命名订单" : Order.Title;
    public string CustomerName => Order.Customer?.Name ?? string.Empty;
    public string CustomerNameDisplay => string.IsNullOrWhiteSpace(Order.Customer?.Name) ? "未知客户" : Order.Customer.Name;
    public string StatusLabel => OrderStatusCatalog.GetLabel(Order.Status);
    public string AmountText => Order.Amount > 0 ? $"¥{Order.Amount:N0}" : "待报价";
    public string PlatformText => string.IsNullOrWhiteSpace(Order.SourcePlatform) ? "未标记" : Order.SourcePlatform;
    public string FollowUpText => Order.NextFollowUpAt?.ToString("MM-dd HH:mm") ?? "无跟进";
    public string RequirementSummary => string.IsNullOrWhiteSpace(Order.Requirement) ? "暂无需求摘要" : Order.Requirement;
    public string NextFollowUpDisplay => Order.NextFollowUpAt?.ToString("yyyy-MM-dd HH:mm") ?? "待安排";
    public string LastActivityDisplay => Order.UpdatedAt > Order.CreatedAt
        ? $"最近更新 {Order.UpdatedAt:yyyy-MM-dd HH:mm}"
        : $"创建于 {Order.CreatedAt:yyyy-MM-dd HH:mm}";
}
