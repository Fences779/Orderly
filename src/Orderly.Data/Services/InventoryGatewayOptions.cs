namespace Orderly.Data.Services;

public sealed class InventoryGatewayOptions
{
    public const string EndpointEnvironmentVariableName = "ORDERLY_INVENTORY_GATEWAY_ENDPOINT";
    public const string TokenEnvironmentVariableName = "ORDERLY_INVENTORY_GATEWAY_TOKEN";
    public const string TimeoutEnvironmentVariableName = "ORDERLY_INVENTORY_GATEWAY_TIMEOUT_SECONDS";
    public const string WorkspaceIdEnvironmentVariableName = "ORDERLY_INVENTORY_WORKSPACE_ID";
    public const string OperatorIdEnvironmentVariableName = "ORDERLY_INVENTORY_OPERATOR_ID";
    public const string AllowedHostsEnvironmentVariableName = "ORDERLY_INVENTORY_GATEWAY_ALLOWED_HOSTS";
    public const string MinTokenLengthEnvironmentVariableName = "ORDERLY_INVENTORY_GATEWAY_MIN_TOKEN_LENGTH";
    public const int DefaultTimeoutSeconds = 15;
    public const int DefaultMinTokenLength = 24;

    public InventoryGatewayOptions(string endpoint, string token, int timeoutSeconds, string workspaceId, string operatorId)
    {
        Endpoint = GatewayConfigurationSafety.NormalizeOptionalValue(
            EndpointEnvironmentVariableName,
            endpoint,
            GatewayConfigurationSafety.MaxEndpointCharacters);
        Token = GatewayConfigurationSafety.NormalizeOptionalValue(
            TokenEnvironmentVariableName,
            token,
            GatewayConfigurationSafety.MaxTokenCharacters);
        TimeoutSeconds = NormalizeTimeout(timeoutSeconds);
        WorkspaceId = GatewayConfigurationSafety.NormalizeValueOrFallback(
            WorkspaceIdEnvironmentVariableName,
            workspaceId,
            "default",
            GatewayConfigurationSafety.MaxIdentifierCharacters);
        OperatorId = GatewayConfigurationSafety.NormalizeValueOrFallback(
            OperatorIdEnvironmentVariableName,
            operatorId,
            Environment.UserName,
            GatewayConfigurationSafety.MaxIdentifierCharacters);
    }

    public string Endpoint { get; }

    public string Token { get; }

    public int TimeoutSeconds { get; }

    public string WorkspaceId { get; }

    public string OperatorId { get; }

    public bool HasEndpoint => !string.IsNullOrWhiteSpace(Endpoint);

    public bool HasToken => !string.IsNullOrWhiteSpace(Token);

    public bool IsConfigured => HasEndpoint && HasToken;

    public static InventoryGatewayOptions FromEnvironment()
    {
        _ = int.TryParse(Environment.GetEnvironmentVariable(TimeoutEnvironmentVariableName), out var timeoutSeconds);
        return new InventoryGatewayOptions(
            Environment.GetEnvironmentVariable(EndpointEnvironmentVariableName) ?? string.Empty,
            Environment.GetEnvironmentVariable(TokenEnvironmentVariableName) ?? string.Empty,
            timeoutSeconds,
            Environment.GetEnvironmentVariable(WorkspaceIdEnvironmentVariableName) ?? string.Empty,
            Environment.GetEnvironmentVariable(OperatorIdEnvironmentVariableName) ?? string.Empty);
    }

    public Uri GetEndpointUri()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            throw new InvalidOperationException($"{EndpointEnvironmentVariableName} 未配置，无法连接库存云端网关。");
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{EndpointEnvironmentVariableName} 必须是有效的绝对 URL。");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
        {
            throw new InvalidOperationException($"{EndpointEnvironmentVariableName} 必须使用 HTTPS，除非目标是本机回环地址。");
        }

        OutboundEndpointPolicy.Validate(uri, EndpointEnvironmentVariableName, AllowedHostsEnvironmentVariableName);
        return uri;
    }

    public void ValidateToken()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            throw new InvalidOperationException($"{TokenEnvironmentVariableName} 未配置，无法调用库存云端网关。");
        }

        var minLength = ReadMinTokenLength();
        if (Token.Length < minLength || IsPlaceholderToken(Token))
        {
            throw new InvalidOperationException($"{TokenEnvironmentVariableName} 强度不足，长度至少需要 {minLength} 位，且不能使用占位 token。");
        }
    }

    private static int NormalizeTimeout(int timeoutSeconds)
    {
        return timeoutSeconds <= 0 ? DefaultTimeoutSeconds : Math.Clamp(timeoutSeconds, 1, 120);
    }

    private static int ReadMinTokenLength()
    {
        _ = int.TryParse(Environment.GetEnvironmentVariable(MinTokenLengthEnvironmentVariableName), out var minLength);
        return minLength <= 0 ? DefaultMinTokenLength : Math.Clamp(minLength, DefaultMinTokenLength, 128);
    }

    private static bool IsPlaceholderToken(string token)
    {
        return token.Trim().ToLowerInvariant() is "replace-me" or "changeme" or "change-me" or "test" or "token" or "password";
    }
}
