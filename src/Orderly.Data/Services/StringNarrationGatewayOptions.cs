namespace Orderly.Data.Services;

public sealed class StringNarrationGatewayOptions
{
    public const string EndpointEnvironmentVariableName = "ADMIN_PC_GATEWAY_ENDPOINT";
    public const string TokenEnvironmentVariableName = "ADMIN_PC_GATEWAY_TOKEN";
    public const string TimeoutEnvironmentVariableName = "ADMIN_PC_GATEWAY_TIMEOUT_SECONDS";
    public const string SendTokenInBodyEnvironmentVariableName = "ADMIN_PC_GATEWAY_SEND_TOKEN_IN_BODY";
    public const int DefaultTimeoutSeconds = 15;

    public StringNarrationGatewayOptions(string endpoint, string token, int timeoutSeconds, bool sendTokenInBody = false)
    {
        Endpoint = endpoint.Trim();
        Token = token.Trim();
        TimeoutSeconds = NormalizeTimeout(timeoutSeconds);
        SendTokenInBody = sendTokenInBody;
    }

    public string Endpoint { get; }

    public string Token { get; }

    public int TimeoutSeconds { get; }

    public bool SendTokenInBody { get; }

    public bool HasEndpoint => !string.IsNullOrWhiteSpace(Endpoint);

    public bool HasToken => !string.IsNullOrWhiteSpace(Token);

    public static StringNarrationGatewayOptions FromEnvironment()
    {
        _ = int.TryParse(Environment.GetEnvironmentVariable(TimeoutEnvironmentVariableName), out var timeoutSeconds);
        return new StringNarrationGatewayOptions(
            Environment.GetEnvironmentVariable(EndpointEnvironmentVariableName) ?? string.Empty,
            Environment.GetEnvironmentVariable(TokenEnvironmentVariableName) ?? string.Empty,
            timeoutSeconds,
            IsEnabled(Environment.GetEnvironmentVariable(SendTokenInBodyEnvironmentVariableName)));
    }

    public Uri GetEndpointUri()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            throw new InvalidOperationException($"{EndpointEnvironmentVariableName} 未配置，无法连接串述 adminPcGateway。");
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{EndpointEnvironmentVariableName} 必须是有效的绝对 URL。");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
        {
            throw new InvalidOperationException($"{EndpointEnvironmentVariableName} 必须使用 HTTPS，除非目标是本机回环地址。");
        }

        OutboundEndpointPolicy.Validate(uri, EndpointEnvironmentVariableName);
        return uri;
    }

    public void ValidateToken()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            throw new InvalidOperationException($"{TokenEnvironmentVariableName} 未配置，无法调用串述 adminPcGateway。");
        }
    }

    private static int NormalizeTimeout(int timeoutSeconds)
    {
        return timeoutSeconds <= 0 ? DefaultTimeoutSeconds : Math.Clamp(timeoutSeconds, 1, 120);
    }

    private static bool IsEnabled(string? value)
    {
        return string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }
}
