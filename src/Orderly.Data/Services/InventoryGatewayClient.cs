using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed class InventoryGatewayClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly InventoryGatewayOptions _options;

    public InventoryGatewayClient(HttpClient httpClient, InventoryGatewayOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<JsonElement> InvokeAsync(string action, object payload, CancellationToken cancellationToken = default)
    {
        var endpoint = _options.GetEndpointUri();
        _options.ValidateToken();

        var requestPayload = new
        {
            action,
            payload
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(requestPayload, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"调用库存云端网关超时（{_options.TimeoutSeconds} 秒）。", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("连接库存云端网关失败，请检查 endpoint、网络和 TLS 配置。", ex);
        }

        using var _ = response;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (TryParseGatewayError(body, out var gatewayError))
            {
                throw new InvalidOperationException(gatewayError);
            }

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException($"库存云端网关未授权或 token 无效（HTTP {(int)response.StatusCode}）。");
            }

            throw new InvalidOperationException($"库存云端网关返回 HTTP {(int)response.StatusCode}。");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("库存云端网关返回的 JSON 无法解析。", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("库存云端网关返回格式不正确：根节点不是对象。");
            }

            if (TryReadBool(root, "ok") == false)
            {
                var code = ReadString(root, "code");
                var message = ReadString(root, "message");
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(code)
                    ? $"库存云端网关调用失败：{message}"
                    : $"库存云端网关调用失败：{code} {message}".Trim());
            }

            return root.Clone();
        }
    }

    private static bool? TryReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool TryParseGatewayError(string body, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var code = ReadString(root, "code");
            var message = ReadString(root, "message");
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            errorMessage = string.IsNullOrWhiteSpace(code)
                ? $"库存云端网关调用失败：{message}"
                : $"库存云端网关调用失败：{code} {message}".Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
