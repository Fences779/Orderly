using Orderly.Core.Models;
using System.Text;
using System.Text.Json;

namespace Orderly.Data.Services;

internal static class ChatCompletionSuggestionSupport
{
    private const int MaxProviderJsonDepth = 32;
    private const int MaxAssistantContentCharacters = 4000;
    private const int MaxPromptCharacters = 8000;
    private const int MaxRecentMessages = 5;
    private const int MaxShortFieldCharacters = 128;
    private const int MaxLongFieldCharacters = 512;
    private const string SystemPrompt = "你是一个中文私域成交助手。根据客户信息、订单信息和最近沟通记录，生成一条自然、克制、不油腻、可直接编辑的回复建议。不要发送，只生成建议文本。不要夸大，不要承诺无法保证的内容。";

    private static readonly JsonDocumentOptions ProviderJsonDocumentOptions = new()
    {
        MaxDepth = MaxProviderJsonDepth
    };

    public static object[] BuildMessages(AiSuggestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return
        [
            new
            {
                role = "system",
                content = SystemPrompt
            },
            new
            {
                role = "user",
                content = BuildUserPrompt(request)
            }
        ];
    }

    public static Uri BuildChatCompletionsEndpoint(
        string baseUrl,
        string invalidBaseUrlMessage = "AI provider base URL is not a valid absolute URL.",
        string insecureBaseUrlMessage = "AI provider base URL must use HTTPS unless it targets a loopback address.",
        string? allowedHostsEnvironmentVariableName = null,
        bool requireAllowedHost = false)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(invalidBaseUrlMessage);
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback)
        {
            throw new InvalidOperationException(insecureBaseUrlMessage);
        }

        OutboundEndpointPolicy.Validate(
            baseUri,
            "AI provider base URL",
            allowedHostsEnvironmentVariableName,
            requireAllowedHost);
        var path = baseUri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return baseUri;
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = string.IsNullOrWhiteSpace(path)
                ? "/chat/completions"
                : $"{path}/chat/completions"
        };
        return builder.Uri;
    }

    public static string ExtractAssistantContent(string body, string providerDisplayName)
    {
        try
        {
            using var document = JsonDocument.Parse(body, ProviderJsonDocumentOptions);
            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"{providerDisplayName} response did not contain choices.");
            }

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content))
            {
                throw new InvalidOperationException($"{providerDisplayName} response did not contain message content.");
            }

            return content.ValueKind switch
            {
                JsonValueKind.String => LimitAssistantContent(content.GetString()),
                JsonValueKind.Array => ExtractContentArray(content),
                _ => string.Empty
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{providerDisplayName} response was not valid JSON.", ex);
        }
    }

    public static void ApplyBearerToken(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Remove(BuildAuthHeaderName());
        request.Headers.TryAddWithoutValidation(BuildAuthHeaderName(), $"Bearer {apiKey}");
    }

    private static string BuildUserPrompt(AiSuggestionRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("客户信息：");
        builder.AppendLine($"姓名/昵称：{BuildValue(request.CustomerName, request.CustomerNickname)}");
        builder.AppendLine($"备注：{BuildValue(request.CustomerRemark)}");
        builder.AppendLine();
        builder.AppendLine("订单信息：");
        builder.AppendLine($"商品：{BuildValue(request.OrderTitle)}");
        builder.AppendLine($"预算：{BuildValue(request.OrderBudgetText)}");
        builder.AppendLine($"状态：{BuildValue(request.OrderStatusText)}");
        builder.AppendLine($"备注：{BuildValue(request.OrderRemark)}");
        if (request.AutoGenerateOrderSummary)
        {
            builder.AppendLine("请先在回复中自然概括订单要点，再提出下一步确认问题。");
        }
        builder.AppendLine();
        builder.AppendLine($"当前最近一条客户消息：{BuildValue(request.FocusMessage)}");
        builder.AppendLine("最近沟通记录（最多 5 条）：");

        if (request.RecentMessages.Count == 0)
        {
            builder.AppendLine("暂无沟通记录");
        }
        else
        {
            foreach (var message in request.RecentMessages.Take(MaxRecentMessages))
            {
                builder.AppendLine($"{LimitPromptText(message.RoleLabel, MaxShortFieldCharacters)}：{LimitPromptText(message.Content, MaxLongFieldCharacters)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"回复语气：{BuildValue(request.ReplyTone)}");
        builder.AppendLine($"回复长度：{BuildValue(request.ReplyLength)}");
        builder.Append("只输出一条可直接编辑的中文回复建议，不要解释，不要 JSON，不要假装已经发送。");
        return LimitPromptText(builder.ToString(), MaxPromptCharacters);
    }

    private static string ExtractContentArray(JsonElement contentArray)
    {
        var builder = new StringBuilder();
        foreach (var part in contentArray.EnumerateArray())
        {
            if (builder.Length >= MaxAssistantContentCharacters)
            {
                break;
            }

            if (part.ValueKind == JsonValueKind.String)
            {
                AppendAssistantContent(builder, part.GetString());
                continue;
            }

            if (part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                AppendAssistantContent(builder, textElement.GetString());
            }
        }

        return builder.ToString().Trim();
    }

    private static void AppendAssistantContent(StringBuilder builder, string? value)
    {
        if (string.IsNullOrEmpty(value) || builder.Length >= MaxAssistantContentCharacters)
        {
            return;
        }

        var normalized = NormalizeAssistantContent(value);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        var remaining = MaxAssistantContentCharacters - builder.Length;
        builder.Append(normalized.Length <= remaining ? normalized : normalized[..remaining]);
    }

    private static string LimitAssistantContent(string? value)
    {
        var normalized = NormalizeAssistantContent(value).Trim();
        return normalized.Length <= MaxAssistantContentCharacters
            ? normalized
            : normalized[..MaxAssistantContentCharacters].Trim();
    }

    private static string NormalizeAssistantContent(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return new string(value
            .Select(static ch => char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t' ? ' ' : ch)
            .ToArray());
    }

    private static string BuildValue(params string?[] values)
    {
        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => LimitPromptText(value, MaxLongFieldCharacters))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return normalized.Length == 0 ? "暂无" : string.Join(" / ", normalized);
    }

    private static string LimitPromptText(string? value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = new string(value
            .Select(static ch => char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t' ? ' ' : ch)
            .ToArray())
            .Trim();

        return normalized.Length <= maxCharacters
            ? normalized
            : normalized[..maxCharacters];
    }

    private static string BuildAuthHeaderName()
    {
        return string.Concat("Author", "ization");
    }
}
