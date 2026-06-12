using System.Text;

namespace Orderly.Data.Services;

public sealed class InventoryGatewayOptions
{
    public const string EndpointEnvironmentVariableName = "ORDERLY_INVENTORY_GATEWAY_ENDPOINT";
    public const string TokenEnvironmentVariableName = "ORDERLY_INVENTORY_GATEWAY_TOKEN";
    public const string ActionTokenEnvironmentVariablePrefix = "ORDERLY_INVENTORY_GATEWAY_TOKEN_";
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

    public bool HasToken => HasActionScopedTokenConfiguration();

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

        OutboundEndpointPolicy.Validate(uri, EndpointEnvironmentVariableName, AllowedHostsEnvironmentVariableName, requireAllowedHost: true);
        return uri;
    }

    public string GetTokenForAction(string action)
    {
        var variableName = BuildActionTokenEnvironmentVariableName(action);
        var actionToken = GatewayConfigurationSafety.NormalizeOptionalValue(
            variableName,
            Environment.GetEnvironmentVariable(variableName) ?? string.Empty,
            GatewayConfigurationSafety.MaxTokenCharacters);
        if (!string.IsNullOrWhiteSpace(actionToken))
        {
            ValidateTokenValue(actionToken, variableName);
            return actionToken;
        }

        throw new InvalidOperationException($"{variableName} 未配置，库存云端网关必须使用按 action 隔离的 token。");
    }

    private static void ValidateTokenValue(string token, string variableName)
    {
        var minLength = ReadMinTokenLength();
        if (token.Length < minLength || IsPlaceholderToken(token))
        {
            throw new InvalidOperationException($"{variableName} 强度不足，长度至少需要 {minLength} 位，且不能使用占位 token。");
        }
    }

    private static string BuildActionTokenEnvironmentVariableName(string action)
    {
        var builder = new StringBuilder(action.Length);
        var previous = '\0';
        foreach (var ch in action)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (char.IsUpper(ch)
                    && builder.Length > 0
                    && previous != '_'
                    && (char.IsLower(previous) || char.IsDigit(previous)))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToUpperInvariant(ch));
                previous = ch;
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
                previous = '_';
            }
        }

        var suffix = builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(suffix))
        {
            throw new InvalidOperationException("库存云端网关 action 无效，无法解析 action token 变量。");
        }

        return ActionTokenEnvironmentVariablePrefix + suffix;
    }

    private static bool HasActionScopedTokenConfiguration()
    {
        foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            if (variable.Key is string name
                && name.StartsWith(ActionTokenEnvironmentVariablePrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, TokenEnvironmentVariableName, StringComparison.OrdinalIgnoreCase)
                && variable.Value is string value
                && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
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
