using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed class StringNarrationGatewayClient
{
    private const long MaxRequestBodyBytes = 256L * 1024L;
    private const long MaxResponseBodyBytes = 1024L * 1024L;
    private const int MaxResponseJsonDepth = 32;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonDocumentOptions ResponseJsonDocumentOptions = new()
    {
        MaxDepth = MaxResponseJsonDepth
    };

    private readonly HttpClient _httpClient;
    private readonly StringNarrationGatewayOptions _options;

    public StringNarrationGatewayClient(HttpClient httpClient, StringNarrationGatewayOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<JsonElement> InvokeAsync(string action, object payload, CancellationToken cancellationToken = default)
    {
        var endpoint = _options.GetEndpointUri();
        _options.ValidateOperatorId();
        var token = _options.GetTokenForAction(action);
        var sendTokenInBody = _options.ShouldSendTokenInBody(endpoint);
        var requestId = Guid.NewGuid().ToString("N");
        var requestTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        object requestPayload = sendTokenInBody
            ? new
            {
                action,
                token,
                operatorId = _options.OperatorId,
                requestId,
                requestTimestamp,
                payload
            }
            : new
            {
                action,
                operatorId = _options.OperatorId,
                requestId,
                requestTimestamp,
                payload
            };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("X-Orderly-Gateway-Action", action);
        request.Headers.TryAddWithoutValidation("X-Orderly-Gateway-Operator-Id", _options.OperatorId);
        request.Headers.TryAddWithoutValidation("X-Orderly-Gateway-Request-Id", requestId);
        request.Headers.TryAddWithoutValidation("X-Orderly-Gateway-Request-Timestamp", requestTimestamp);
        var requestJson = JsonSerializer.Serialize(requestPayload, JsonOptions);
        if (Encoding.UTF8.GetByteCount(requestJson) > MaxRequestBodyBytes)
        {
            throw new InvalidOperationException("串述 adminPcGateway 请求体超过安全上限。");
        }

        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"调用串述 adminPcGateway 超时（{_options.TimeoutSeconds} 秒）。", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("连接串述 adminPcGateway 失败，请检查 endpoint、网络和 TLS 配置。", ex);
        }

        using var _ = response;
        var body = await HttpContentSafety.ReadAsStringWithLimitAsync(
            response.Content,
            MaxResponseBodyBytes,
            "串述 adminPcGateway",
            cancellationToken,
            TimeSpan.FromSeconds(_options.TimeoutSeconds));
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException($"串述 adminPcGateway 未授权或 token 无效（HTTP {(int)response.StatusCode}）。");
            }

            if (TryReadGatewayErrorCode(body, out var gatewayErrorCode))
            {
                throw new InvalidOperationException($"串述 adminPcGateway 调用失败：{gatewayErrorCode}");
            }

            throw new InvalidOperationException($"串述 adminPcGateway 返回 HTTP {(int)response.StatusCode}。");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body, ResponseJsonDocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("串述 adminPcGateway 返回的 JSON 无法解析。", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("串述 adminPcGateway 返回格式不正确：根节点不是对象。");
            }

            if (TryReadBool(root, "ok") == false)
            {
                var code = NormalizeGatewayErrorCode(ReadString(root, "code"));
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(code)
                    ? "串述 adminPcGateway 调用失败。"
                    : $"串述 adminPcGateway 调用失败：{code}");
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
            JsonValueKind.Number => NormalizeGatewayErrorCode(property.GetRawText()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool TryReadGatewayErrorCode(string body, out string errorCode)
    {
        errorCode = string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body, ResponseJsonDocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var code = ReadString(root, "code");
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            errorCode = NormalizeGatewayErrorCode(code);
            if (string.IsNullOrWhiteSpace(errorCode))
            {
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeGatewayErrorCode(string value)
    {
        var code = value.Trim();
        if (code.Length == 0 || code.Length > 64)
        {
            return string.Empty;
        }

        foreach (var ch in code)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not '_' and not '-' and not '.')
            {
                return string.Empty;
            }
        }

        return code;
    }

}
