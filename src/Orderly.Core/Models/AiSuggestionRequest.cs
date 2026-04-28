namespace Orderly.Core.Models;

public sealed class AiSuggestionRequest
{
    public int CustomerId { get; set; }
    public int? OrderId { get; set; }
    public int? MessageId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerNickname { get; set; } = string.Empty;
    public string CustomerRemark { get; set; } = string.Empty;
    public string OrderTitle { get; set; } = string.Empty;
    public string OrderBudgetText { get; set; } = string.Empty;
    public string OrderStatusText { get; set; } = string.Empty;
    public string OrderRemark { get; set; } = string.Empty;
    public string FocusMessage { get; set; } = string.Empty;
    public IReadOnlyList<AiSuggestionContextMessage> RecentMessages { get; set; } = Array.Empty<AiSuggestionContextMessage>();
}
