using System.Globalization;
using System.Windows.Data;
using Orderly.Core.Models;

namespace Orderly.App.Converters;

public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            NoteType.General => "通用",
            NoteType.Requirement => "需求",
            NoteType.Preference => "偏好",
            NoteType.Risk => "风险",
            NoteType.Internal => "内部",
            FollowUpStatus.Pending => "待跟进",
            FollowUpStatus.InProgress => "进行中",
            FollowUpStatus.Completed => "已完成",
            FollowUpStatus.Skipped => "已跳过",
            FollowUpStatus.Cancelled => "已取消",
            FollowUpStatus.Overdue => "已逾期",
            PriceAdjustmentStatus.Draft => "草稿",
            PriceAdjustmentStatus.PendingApproval => "待审批",
            PriceAdjustmentStatus.Approved => "已通过",
            PriceAdjustmentStatus.Rejected => "已驳回",
            PriceAdjustmentStatus.Applied => "已生效",
            PriceAdjustmentStatus.Cancelled => "已取消",
            DealStage.New => "新建",
            DealStage.Qualified => "已确认",
            DealStage.Quoting => "报价中",
            DealStage.Negotiating => "谈判中",
            DealStage.Won => "已成交",
            DealStage.Lost => "已丢单",
            DealStage.Archived => "已归档",
            CustomerStatus status => CustomerStatusCatalog.GetLabel(status),
            CustomerPriority.Low => "低",
            CustomerPriority.Normal => "普通",
            CustomerPriority.High => "高",
            CustomerPriority.Critical => "紧急",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
