using System.Globalization;
using System.Windows.Data;
using Orderly.Core.Commerce;

namespace Orderly.App.Converters;

/// <summary>
/// Renders the universal Commerce domain enums (order stage dimensions, cash-flow direction and
/// settlement status, insight severity, product type) as Simplified Chinese user-facing labels so
/// the commerce pages never render an English enum name (Req 6.1, 17.3).
/// </summary>
public sealed class CommerceEnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            OrderSalesStage.Draft => "草稿",
            OrderSalesStage.Quoted => "已报价",
            OrderSalesStage.Confirmed => "已确认",
            OrderSalesStage.Completed => "已完成",
            OrderSalesStage.Cancelled => "已取消",

            OrderPaymentStage.Unpaid => "未付款",
            OrderPaymentStage.PartiallyPaid => "部分付款",
            OrderPaymentStage.Paid => "已付款",
            OrderPaymentStage.Refunded => "已退款",

            OrderFulfillmentStage.NotStarted => "未开始",
            OrderFulfillmentStage.InProgress => "进行中",
            OrderFulfillmentStage.Ready => "待交付",
            OrderFulfillmentStage.Fulfilled => "已履约",
            OrderFulfillmentStage.Returned => "已退货",

            CashFlowDirection.Income => "收入",
            CashFlowDirection.Expense => "支出",
            CashFlowDirection.Transfer => "转账",

            CashFlowSettlementStatus.Settled => "已结清",
            CashFlowSettlementStatus.Pending => "待结算",
            CashFlowSettlementStatus.PartiallySettled => "部分结算",
            CashFlowSettlementStatus.Overdue => "已逾期",

            InsightSeverity.Info => "提示",
            InsightSeverity.Warning => "关注",
            InsightSeverity.Critical => "紧急",

            ProductType.Physical => "实物",
            ProductType.Service => "服务",
            ProductType.Digital => "数字",
            ProductType.Bundle => "组合",

            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
