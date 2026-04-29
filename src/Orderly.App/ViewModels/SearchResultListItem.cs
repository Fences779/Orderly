using Orderly.App.ViewModels.Helpers;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public sealed class SearchResultListItem
{
    public SearchResultListItem(SearchResultItem result)
    {
        Result = result;
    }

    public SearchResultItem Result { get; }
    public string Id => Result.Id;
    public SearchResultType Type => Result.Type;
    public string Title => Result.Title;
    public string Summary => Result.Summary;
    public int? CustomerId => Result.CustomerId;
    public string CustomerName => Result.CustomerName;
    public int? OrderId => Result.OrderId;
    public string RelatedEntityType => Result.RelatedEntityType;
    public int? RelatedEntityId => Result.RelatedEntityId;
    public string MatchedField => Result.MatchedField;
    public int Score => Result.Score;
    public string TargetSection => Result.TargetSection;
    public string ActionHint => Result.ActionHint;
    public bool HasPipelineStage => Result.PipelineStage is not null;
    public string PipelineStageText => Result.PipelineStage is PipelineStage stage ? StatusLabelHelper.GetPipelineStageLabel(stage) : "未推导";
    public string TypeText => GetTypeText();
    public string OccurredAtText => Result.OccurredAt.ToString("MM-dd HH:mm");
    public string ContextText => Result.OrderId is > 0
        ? $"{GetCustomerDisplay()} / 订单 #{Result.OrderId}"
        : GetCustomerDisplay();

    private string GetTypeText()
    {
        return Result.Type switch
        {
            SearchResultType.Customer => "客户",
            SearchResultType.Order => "订单",
            SearchResultType.ConversationMessage => "沟通记录",
            SearchResultType.AiSuggestion => "AI 建议",
            SearchResultType.OcrResult => "OCR 结果",
            SearchResultType.FollowUp => "跟进",
            SearchResultType.ActivityLog => "活动日志",
            _ => Result.Type.ToString()
        };
    }

    private string GetCustomerDisplay()
    {
        return string.IsNullOrWhiteSpace(Result.CustomerName)
            ? "未定位客户"
            : Result.CustomerName;
    }
}
