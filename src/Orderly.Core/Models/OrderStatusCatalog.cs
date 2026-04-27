namespace Orderly.Core.Models;

public static class OrderStatusCatalog
{
    public static string GetLabel(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.PendingCommunication => "待沟通",
            OrderStatus.PendingQuote => "待报价",
            OrderStatus.Quoted => "已报价",
            OrderStatus.PendingFollowUp => "待跟进",
            OrderStatus.Won => "已成交",
            OrderStatus.Closed => "已关闭",
            _ => "未知"
        };
    }
}
