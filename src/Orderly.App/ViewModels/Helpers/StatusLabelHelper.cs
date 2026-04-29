using Orderly.Core.Models;

namespace Orderly.App.ViewModels.Helpers;

internal static class StatusLabelHelper
{
    public static string GetCustomerStatusLabel(CustomerStatus status)
    {
        return CustomerStatusCatalog.GetLabel(status);
    }

    public static string GetCustomerPriorityLabel(CustomerPriority priority)
    {
        return priority switch
        {
            CustomerPriority.Low => "低",
            CustomerPriority.Normal => "普通",
            CustomerPriority.High => "高",
            CustomerPriority.Critical => "紧急",
            _ => priority.ToString()
        };
    }

    public static string GetDealStageLabel(DealStage stage)
    {
        return stage switch
        {
            DealStage.New => "新建",
            DealStage.Qualified => "已确认",
            DealStage.Quoting => "报价中",
            DealStage.Negotiating => "谈判中",
            DealStage.Won => "已成交",
            DealStage.Lost => "已丢单",
            DealStage.Archived => "已归档",
            _ => stage.ToString()
        };
    }

    public static string GetFollowUpStatusLabel(FollowUpStatus status)
    {
        return status switch
        {
            FollowUpStatus.Pending => "待跟进",
            FollowUpStatus.InProgress => "进行中",
            FollowUpStatus.Completed => "已完成",
            FollowUpStatus.Skipped => "已跳过",
            FollowUpStatus.Cancelled => "已取消",
            FollowUpStatus.Overdue => "已逾期",
            _ => status.ToString()
        };
    }

    public static string GetPipelineStageLabel(PipelineStage stage)
    {
        return stage switch
        {
            PipelineStage.New => "新线索",
            PipelineStage.Contacted => "已沟通",
            PipelineStage.Interested => "有意向",
            PipelineStage.Quoted => "已报价",
            PipelineStage.DraftPrepared => "草稿已备",
            PipelineStage.WaitingPayment => "待付款",
            PipelineStage.Paid => "已成交",
            PipelineStage.Fulfilled => "已履约",
            PipelineStage.Lost => "已流失",
            _ => stage.ToString()
        };
    }

    public static DealStage GetNextStage(DealStage stage)
    {
        return stage switch
        {
            DealStage.New => DealStage.Qualified,
            DealStage.Qualified => DealStage.Quoting,
            DealStage.Quoting => DealStage.Negotiating,
            DealStage.Negotiating => DealStage.Won,
            DealStage.Won => DealStage.Archived,
            DealStage.Lost => DealStage.Archived,
            _ => DealStage.New
        };
    }
}
