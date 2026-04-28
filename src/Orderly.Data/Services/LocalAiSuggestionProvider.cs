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
        var suggestionText = string.IsNullOrWhiteSpace(focusMessage)
            ? "【Local Stub】我先帮你整理需求。方便补充一下尺寸、数量、预算和期望交付时间，我再给你更准确的方案。"
            : $"【Local Stub】收到，你的需求我先记下了。关于“{focusMessage}”，我先确认一下尺寸、数量、预算和期望交付时间，再给你更准确的方案。";

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
