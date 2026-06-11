namespace Orderly.Data.Services;

public sealed class StringNarrationGatewayOptions
{
    public const string EndpointEnvironmentVariableName = "ADMIN_PC_GATEWAY_ENDPOINT";
    public const string TokenEnvironmentVariableName = "ADMIN_PC_GATEWAY_TOKEN";
    public const string ActionTokenEnvironmentVariablePrefix = "ADMIN_PC_GATEWAY_TOKEN_";
    public const string OperatorIdEnvironmentVariableName = "ADMIN_PC_GATEWAY_OPERATOR_ID";
    public const string TimeoutEnvironmentVariableName = "ADMIN_PC_GATEWAY_TIMEOUT_SECONDS";
    public const string SendTokenInBodyEnvironmentVariableName = "ADMIN_PC_GATEWAY_SEND_TOKEN_IN_BODY";
    public const string AllowedHostsEnvironmentVariableName = "ADMIN_PC_GATEWAY_ALLOWED_HOSTS";
    public const string MinTokenLengthEnvironmentVariableName = "ADMIN_PC_GATEWAY_MIN_TOKEN_LENGTH";
    public const int DefaultTimeoutSeconds = 15;
    public const int DefaultMinTokenLength = 24;

    public StringNarrationGatewayOptions(string endpoint, string token, int timeoutSeconds, bool sendTokenInBody = false, string operatorId = "")
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
        SendTokenInBody = sendTokenInBody;
        OperatorId = NormalizeOperatorId(operatorId);
    }

    public string Endpoint { get; }

    public string Token { get; }

    public string OperatorId { get; }

    public int TimeoutSeconds { get; }

    public bool SendTokenInBody { get; }

    public bool HasEndpoint => !string.IsNullOrWhiteSpace(Endpoint);

    public bool HasToken => !string.IsNullOrWhiteSpace(Token) || HasActionScopedTokenConfiguration();

    public static StringNarrationGatewayOptions FromEnvironment()
    {
        _ = int.TryParse(Environment.GetEnvironmentVariable(TimeoutEnvironmentVariableName), out var timeoutSeconds);
        return new StringNarrationGatewayOptions(
            Environment.GetEnvironmentVariable(EndpointEnvironmentVariableName) ?? string.Empty,
            Environment.GetEnvironmentVariable(TokenEnvironmentVariableName) ?? string.Empty,
            timeoutSeconds,
            IsEnabled(Environment.GetEnvironmentVariable(SendTokenInBodyEnvironmentVariableName)),
            Environment.GetEnvironmentVariable(OperatorIdEnvironmentVariableName) ?? string.Empty);
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

        OutboundEndpointPolicy.Validate(uri, EndpointEnvironmentVariableName, AllowedHostsEnvironmentVariableName);
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

        throw new InvalidOperationException($"{variableName} 未配置，adminPcGateway 必须使用按 action 隔离的 token。");
    }

    public void ValidateOperatorId()
    {
        if (string.IsNullOrWhiteSpace(OperatorId) || IsPlaceholderOperatorId(OperatorId))
        {
            throw new InvalidOperationException($"{OperatorIdEnvironmentVariableName} 未配置或仍使用默认高权限占位身份。");
        }
    }

    public void ValidateToken()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            throw new InvalidOperationException($"{TokenEnvironmentVariableName} 未配置，无法调用串述 adminPcGateway。");
        }

        ValidateTokenValue(Token, TokenEnvironmentVariableName);
    }

    private static void ValidateTokenValue(string token, string variableName)
    {
        var minLength = ReadMinTokenLength();
        if (token.Length < minLength || IsPlaceholderToken(token))
        {
            throw new InvalidOperationException($"{variableName} 强度不足，长度至少需要 {minLength} 位，且不能使用占位 token。");
        }
    }

    public bool ShouldSendTokenInBody(Uri endpoint)
    {
        if (!SendTokenInBody)
        {
            return false;
        }

        if (endpoint.IsLoopback)
        {
            return true;
        }

        throw new InvalidOperationException($"{SendTokenInBodyEnvironmentVariableName} 只能用于本机回环调试，生产链路必须通过 Authorization header 传递 token。");
    }

    private static int NormalizeTimeout(int timeoutSeconds)
    {
        return timeoutSeconds <= 0 ? DefaultTimeoutSeconds : Math.Clamp(timeoutSeconds, 1, 120);
    }

    private static string NormalizeOperatorId(string value)
    {
        var operatorId = GatewayConfigurationSafety.NormalizeOptionalValue(
            OperatorIdEnvironmentVariableName,
            value,
            maxCharacters: 128);
        if (operatorId.Length == 0)
        {
            return string.Empty;
        }

        return operatorId.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':')
            ? operatorId
            : string.Empty;
    }

    private static string BuildActionTokenEnvironmentVariableName(string action)
    {
        var suffix = new string(action.Select(static ch => char.IsAsciiLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_').ToArray());
        suffix = string.Join("_", suffix.Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(suffix))
        {
            throw new InvalidOperationException("adminPcGateway action 无效，无法解析 action token 变量。");
        }

        return ActionTokenEnvironmentVariablePrefix + suffix;
    }

    private static bool IsEnabled(string? value)
    {
        return string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
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

    private static int ReadMinTokenLength()
    {
        _ = int.TryParse(Environment.GetEnvironmentVariable(MinTokenLengthEnvironmentVariableName), out var minLength);
        return minLength <= 0 ? DefaultMinTokenLength : Math.Clamp(minLength, DefaultMinTokenLength, 128);
    }

    private static bool IsPlaceholderToken(string token)
    {
        return token.Trim().ToLowerInvariant() is "replace-me" or "changeme" or "change-me" or "test" or "token" or "password";
    }

    private static bool IsPlaceholderOperatorId(string operatorId)
    {
        return operatorId.Trim().ToLowerInvariant() is "pc-admin" or "admin" or "administrator" or "root" or "test";
    }
}
