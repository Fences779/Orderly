using Orderly.App.ViewModels.Helpers;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    public MerchantOrder? SelectedOrder => SelectedOrderItem?.Order;
    public string SelectedStatusLabel => SelectedOrder is null ? string.Empty : OrderStatusCatalog.GetLabel(SelectedOrder.Status);
    public string CustomerStatusLabel => SelectedCustomer is null ? string.Empty : StatusLabelHelper.GetCustomerStatusLabel(SelectedCustomer.Status);
    public string CustomerPriorityLabel => SelectedCustomer is null ? string.Empty : StatusLabelHelper.GetCustomerPriorityLabel(SelectedCustomer.Priority);
    public string CurrentDealStage => SelectedDeal is null ? "无" : StatusLabelHelper.GetDealStageLabel(SelectedDeal.Stage);
    public string LatestNote => CustomerNotes.FirstOrDefault()?.Content ?? "暂无备注";
    public string SelectedCustomerNameText => string.IsNullOrWhiteSpace(SelectedCustomer?.Name) ? "未选择客户" : SelectedCustomer.Name;
    public string SelectedOrderHeadline => string.IsNullOrWhiteSpace(SelectedOrder?.Title) ? "未选择订单" : SelectedOrder.Title;
    public string SelectedOrderRequirementSummary => string.IsNullOrWhiteSpace(SelectedOrder?.Requirement)
        ? "当前订单暂无需求摘要"
        : SelectedOrder.Requirement;
    public string SelectedOrderStatusText => SelectedOrder is null ? "未选择订单" : SelectedStatusLabel;
    public string SelectedOrderAmountText => SelectedOrder is null
        ? "金额待确认"
        : SelectedOrder.Amount > 0 ? $"¥{SelectedOrder.Amount:N0}" : "待报价";
    public string SelectedNextFollowUpText => SelectedOrder?.NextFollowUpAt?.ToString("yyyy-MM-dd HH:mm") ?? "暂无安排";
    public string SelectedOrderRequirementText => string.IsNullOrWhiteSpace(SelectedOrder?.Requirement)
        ? "暂无需求记录"
        : SelectedOrder.Requirement;
    public string SelectedSourcePlatformText => string.IsNullOrWhiteSpace(SelectedOrder?.SourcePlatform)
        ? "未标记来源"
        : SelectedOrder.SourcePlatform;
    public string SelectedChannelText => string.IsNullOrWhiteSpace(SelectedOrder?.Channel)
        ? "未标记渠道"
        : SelectedOrder.Channel;
    public string SelectedExternalIdText => string.IsNullOrWhiteSpace(SelectedOrder?.ExternalId)
        ? "未关联外部ID"
        : SelectedOrder.ExternalId;
    public string SelectedConversationContextText => SelectedOrder is not null
        ? $"当前订单：{SelectedOrder.Title}"
        : SelectedCustomer is not null
            ? $"当前客户：{SelectedCustomer.Name}"
            : "请先选择客户或订单";
    public string CustomerRemarkText => string.IsNullOrWhiteSpace(SelectedCustomer?.Remark)
        ? "暂无客户备注"
        : SelectedCustomer.Remark;
    public bool IsStatusError =>
        StatusMessage.Contains("失败", StringComparison.Ordinal) ||
        StatusMessage.Contains("错误", StringComparison.Ordinal);
    public string StatusMessageTitle => IsStatusError ? "需要处理" : "当前状态";
    public bool HasSelectedCustomer => SelectedCustomer is not null;
    public bool HasSelectedOrder => SelectedOrder is not null;
    public bool HasDeals => Deals.Count > 0;
    public bool HasFollowUps => FollowUps.Count > 0;
    public bool HasCustomerNotes => CustomerNotes.Count > 0;
    public bool HasConversationMessages => ConversationMessages.Count > 0;
    public bool HasCurrentOcrResult => CurrentOcrResult is not null;
    public bool HasAiSuggestions => AiSuggestions.Count > 0;
    public bool HasPriceAdjustments => PriceAdjustments.Count > 0;
    public bool HasActivityLogs => ActivityLogs.Count > 0;
    public bool HasCustomers => Customers.Count > 0;
    public bool HasOrders => Orders.Count > 0;
    public string CurrentOcrFileNameText => string.IsNullOrWhiteSpace(CurrentOcrResult?.SourceName) ? "未选择图片" : CurrentOcrResult.SourceName;
    public string CurrentOcrStatusText => CurrentOcrResult is null
        ? "未开始"
        : CurrentOcrResult.Status switch
        {
            OcrStatus.Pending => "Pending",
            OcrStatus.Completed => "Completed",
            OcrStatus.Failed => "Failed",
            _ => CurrentOcrResult.Status.ToString()
        };
    public string CurrentOcrPreviewText => CurrentOcrResult is null
        ? "还没有 OCR 结果。"
        : !string.IsNullOrWhiteSpace(CurrentOcrResult.ExtractedText)
            ? CurrentOcrResult.ExtractedText
            : CurrentOcrResult.Status == OcrStatus.Failed
                ? (string.IsNullOrWhiteSpace(CurrentOcrResult.ErrorMessage) ? "OCR 执行失败。" : CurrentOcrResult.ErrorMessage)
                : "OCR 尚未产出文本。";
    public string CurrentOcrHintText => CurrentOcrResult is null
        ? "请选择图片或截图后执行 OCR。"
        : CurrentOcrResult.Status == OcrStatus.Pending
            ? "OCR 处理中，请等待结果返回。"
            : CurrentOcrResult.Status == OcrStatus.Failed
                ? "OCR 失败，请重新选择图片。"
                : IsCurrentOcrConverted
                    ? "当前 OCR 结果已转为沟通记录。"
                    : "OCR 文本确认无误后，可转为沟通记录。";
    public bool IsCurrentOcrConverted => TryGetCurrentOcrConvertedMessageId() is int convertedMessageId && convertedMessageId > 0;
    public int DealsCount => Deals.Count;
    public int FollowUpsCount => FollowUps.Count;
    public int CustomerNotesCount => CustomerNotes.Count;
    public int ConversationMessagesCount => ConversationMessages.Count;
    public int AiSuggestionsCount => AiSuggestions.Count;
    public int PriceAdjustmentsCount => PriceAdjustments.Count;
    public int ActivityLogsCount => ActivityLogs.Count;
    public bool IsBusy => IsLoading || IsSaving || IsGeneratingAiSuggestion;
    public string OrderDetailsEmptyMessage => SelectedCustomer is null ? "请选择订单或客户" : "当前客户暂无关联订单";
    public int PendingCount => _allOrders.Count(item => FollowUpDateHelper.IsPendingOrder(item.Order.Status));
    public int WonCount => _allOrders.Count(item => item.Order.Status == OrderStatus.Won);
    public decimal TotalAmount => _allOrders.Sum(item => item.Order.Amount);
    public string CustomersCountText => $"{Customers.Count} 个客户";
    public string OrdersCountText => $"{Orders.Count} 个订单";
    public string CustomersEmptyStateText => IsStatusError
        ? $"客户列表暂时不可用\n{StatusMessage}"
        : "还没有客户记录\n本地数据加载完成后会显示在这里";
    public string OrdersEmptyStateText => IsStatusError
        ? $"订单列表暂时不可用\n{StatusMessage}"
        : "还没有订单记录\n本地数据加载完成后会显示在这里";

    private void ClearDetails()
    {
        Deals.Clear();
        FollowUps.Clear();
        CustomerNotes.Clear();
        ConversationMessages.Clear();
        CurrentOcrResult = null;
        AiSuggestions.Clear();
        PriceAdjustments.Clear();
        ActivityLogs.Clear();
        SelectedDeal = null;
        SelectedAiSuggestion = null;
        OnDetailStateChanged();
    }

    private void OnSummaryChanged()
    {
        OnPropertyChanged(nameof(HasCustomers));
        OnPropertyChanged(nameof(HasOrders));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(WonCount));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(CustomersCountText));
        OnPropertyChanged(nameof(OrdersCountText));
        OnPropertyChanged(nameof(CustomersEmptyStateText));
        OnPropertyChanged(nameof(OrdersEmptyStateText));
    }

    private void OnDetailStateChanged()
    {
        OnPropertyChanged(nameof(CurrentDealStage));
        OnPropertyChanged(nameof(LatestNote));
        OnPropertyChanged(nameof(HasDeals));
        OnPropertyChanged(nameof(HasFollowUps));
        OnPropertyChanged(nameof(HasCustomerNotes));
        OnPropertyChanged(nameof(HasConversationMessages));
        OnPropertyChanged(nameof(HasCurrentOcrResult));
        OnPropertyChanged(nameof(HasAiSuggestions));
        OnPropertyChanged(nameof(HasPriceAdjustments));
        OnPropertyChanged(nameof(HasActivityLogs));
        OnPropertyChanged(nameof(DealsCount));
        OnPropertyChanged(nameof(FollowUpsCount));
        OnPropertyChanged(nameof(CustomerNotesCount));
        OnPropertyChanged(nameof(ConversationMessagesCount));
        OnPropertyChanged(nameof(AiSuggestionsCount));
        OnPropertyChanged(nameof(PriceAdjustmentsCount));
        OnPropertyChanged(nameof(ActivityLogsCount));
        NotifyAiSuggestionCommandStateChanged();
        NotifyAutoReplyCommandStateChanged();
    }
}
