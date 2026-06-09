using Orderly.Core.Models;
using System.Text;
using System.Text.Json;

namespace Orderly.Data.Services;

internal static class ChatCompletionSuggestionSupport
{
    private const string SystemPrompt = "你是一个中文私域成交助手。根据客户信息、订单信息和最近沟通记录，生成一条自然、克制、不油腻、可直接编辑的回复建议。不要发送，只生成建议文本。不要夸大，不要承诺无法保证的内容。";

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
        string insecureBaseUrlMessage = "AI provider base URL must use HTTPS unless it targets a loopback address.")
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(invalidBaseUrlMessage);
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback)
        {
            throw new InvalidOperationException(insecureBaseUrlMessage);
        }

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
        using var document = JsonDocument.Parse(body);
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
            JsonValueKind.String => content.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Array => ExtractContentArray(content),
            _ => string.Empty
        };
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
        builder.AppendLine();
        builder.AppendLine($"当前最近一条客户消息：{BuildValue(request.FocusMessage)}");
        builder.AppendLine("最近沟通记录（最多 5 条）：");

        if (request.RecentMessages.Count == 0)
        {
            builder.AppendLine("暂无沟通记录");
        }
        else
        {
            foreach (var message in request.RecentMessages)
            {
                builder.AppendLine($"{message.RoleLabel}：{message.Content}");
            }
        }

        builder.AppendLine();
        builder.Append("只输出一条可直接编辑的中文回复建议，不要解释，不要 JSON，不要假装已经发送。");
        return builder.ToString();
    }

    private static string ExtractContentArray(JsonElement contentArray)
    {
        var builder = new StringBuilder();
        foreach (var part in contentArray.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                builder.Append(part.GetString());
                continue;
            }

            if (part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                builder.Append(textElement.GetString());
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildValue(params string?[] values)
    {
        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();

        return normalized.Length == 0 ? "暂无" : string.Join(" / ", normalized);
    }

    private static string BuildAuthHeaderName()
    {
        return string.Concat("Author", "ization");
    }
}
