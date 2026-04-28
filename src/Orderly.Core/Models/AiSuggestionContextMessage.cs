namespace Orderly.Core.Models;

public sealed class AiSuggestionContextMessage
{
    public string RoleLabel { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime MessageTime { get; set; }
}
