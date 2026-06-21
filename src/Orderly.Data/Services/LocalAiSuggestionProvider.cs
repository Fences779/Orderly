using Orderly.Core.Models;
using Orderly.Core.Services;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed class LocalAiSuggestionProvider : IAiSuggestionProvider
{
    public string Name => "local-stub";

    public Task<AiSuggestionProviderResult> GenerateAsync(AiSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        var focusMessage = BuildSnippet(request.FocusMessage);
        var prefix = request.ReplyTone switch
        {
            "温和" => "收到，我先帮你梳理一下。",
            "专业" => "已收到需求信息，我先确认关键参数。",
            _ => "收到，你的需求我先记下了。"
        };
        var detail = request.ReplyLength switch
        {
            "短" => "请补充尺寸、数量和交付时间。",
            "详细" => "方便补充尺寸、数量、预算、使用场景和期望交付时间，我再给你整理更准确的方案。",
            _ => "方便补充一下尺寸、数量、预算和期望交付时间，我再给你更准确的方案。"
        };
        var summary = request.AutoGenerateOrderSummary && !string.IsNullOrWhiteSpace(request.OrderTitle)
            ? $"我理解这次主要是“{BuildSnippet(request.OrderTitle)}”。"
            : string.Empty;

        var suggestionText = string.IsNullOrWhiteSpace(focusMessage)
            ? $"【Local Stub】{prefix}{summary}{detail}"
            : $"【Local Stub】{prefix}关于“{focusMessage}”，{summary}{detail}";

        var result = new AiSuggestionProviderResult
        {
            Provider = Name,
            Model = "local-stub",
            SuggestionText = suggestionText,
            MetadataJson = JsonSerializer.Serialize(new
            {
                localOnly = true,
                request.RecentMessages.Count
            })
        };

        return Task.FromResult(result);
    }

    private static string BuildSnippet(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Trim();
        return normalized.Length <= 32 ? normalized : $"{normalized[..32]}...";
    }
}
