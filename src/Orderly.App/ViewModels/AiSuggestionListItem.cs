using Orderly.Core.Models;
using System.Text.Json;

namespace Orderly.App.ViewModels;

public sealed class AiSuggestionListItem
{
    private readonly string? _autoReplyState;
    private readonly string _providerBadgeText;

    public AiSuggestionListItem(AiSuggestion suggestion)
    {
        Suggestion = suggestion;
        _autoReplyState = ReadAutoReplyState(suggestion.MetadataJson);
        _providerBadgeText = ReadProviderBadgeText(suggestion.MetadataJson);
    }

    public AiSuggestion Suggestion { get; }
    public int Id => Suggestion.Id;
    public AiSuggestionStatus Status => Suggestion.Status;
    public string SuggestionText => Suggestion.SuggestionText;
    public string Reason => Suggestion.Reason;
    public string CreatedAtText => Suggestion.CreatedAt.ToString("MM-dd HH:mm");
    public string StatusText => GetStatusText();
    public bool CanReview => Suggestion.Status == AiSuggestionStatus.Draft;
    public bool CanPrepareDraft => Suggestion.Status is AiSuggestionStatus.Draft or AiSuggestionStatus.Accepted;
    public bool CanMarkSent => Suggestion.Status == AiSuggestionStatus.DraftPrepared;
    public bool CanRejectDraft => Suggestion.Status == AiSuggestionStatus.DraftPrepared;
    public string DraftStateHint => GetDraftStateHint();
    public string ProviderBadgeText => _providerBadgeText;

    private string GetStatusText()
    {
        return Suggestion.Status switch
        {
            AiSuggestionStatus.Draft => "待处理",
            AiSuggestionStatus.Accepted => "已接受建议",
            AiSuggestionStatus.DraftPrepared => "本地草稿 / 未发送",
            AiSuggestionStatus.Sent when IsAutoReplyDraft => "草稿已标记发送",
            AiSuggestionStatus.Rejected when IsAutoReplyDraft => "草稿已拒绝",
            AiSuggestionStatus.Rejected => "已拒绝",
            AiSuggestionStatus.Sent => "已发送",
            _ => Suggestion.Status.ToString()
        };
    }

    private string GetDraftStateHint()
    {
        return Suggestion.Status switch
        {
            AiSuggestionStatus.DraftPrepared => "该内容已保存为本地回复草稿，未连接微信/闲鱼发送。",
            AiSuggestionStatus.Sent when IsAutoReplyDraft => "这里只是本地标记为已发送，没有执行任何外部平台发送。",
            AiSuggestionStatus.Rejected when IsAutoReplyDraft => "该本地回复草稿已拒绝，记录保留未删除。",
            _ => string.Empty
        };
    }

    private bool IsAutoReplyDraft => !string.IsNullOrWhiteSpace(_autoReplyState);

    private static string? ReadAutoReplyState(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty("autoReply", out var autoReply))
            {
                return null;
            }

            return autoReply.TryGetProperty("state", out var stateElement)
                ? stateElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadProviderBadgeText(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return "Local Stub";
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var provider = document.RootElement.TryGetProperty("provider", out var providerElement)
                ? providerElement.GetString()
                : null;
            var usedFallback = document.RootElement.TryGetProperty("usedFallback", out var fallbackElement)
                && fallbackElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                && fallbackElement.GetBoolean();

            var label = provider switch
            {
                "openai-compatible" => "OpenAI-compatible",
                "deepseek" => "DeepSeek",
                "local-stub" => "Local Stub",
                _ when !string.IsNullOrWhiteSpace(provider) => provider!,
                _ => "Local Stub"
            };

            return usedFallback ? $"{label} · Fallback" : label;
        }
        catch (JsonException)
        {
            return "Local Stub";
        }
    }
}
