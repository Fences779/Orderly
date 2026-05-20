namespace Orderly.Data.Services;

public sealed class AiProviderOptions
{
    public const string LocalProviderName = "local";
    public const string OpenAiCompatibleProviderName = "openai-compatible";
    public const string DeepSeekProviderName = "deepseek";
    public const string DeepSeekBaseUrl = "https://api.deepseek.com";
    public const string DeepSeekDefaultModel = "deepseek-chat";
    public const string DeepSeekApiKeyEnvironmentVariableName = "DEEPSEEK_API_KEY";
    public const int MinTimeoutSeconds = 5;
    public const int MaxTimeoutSeconds = 120;
    public const int DefaultTimeoutSeconds = 15;

    public AiProviderOptions(
        string? requestedProvider,
        string? baseUrl,
        string? apiKey,
        string? model,
        int timeoutSeconds)
    {
        RequestedProvider = NormalizeProvider(requestedProvider);
        BaseUrl = baseUrl?.Trim() ?? string.Empty;
        ApiKey = apiKey?.Trim() ?? string.Empty;
        Model = model?.Trim() ?? string.Empty;
        TimeoutSeconds = NormalizeTimeout(timeoutSeconds);
    }

    public string RequestedProvider { get; }

    public string BaseUrl { get; }

    public string ApiKey { get; }

    public string Model { get; }

    public int TimeoutSeconds { get; }

    public static AiProviderOptions FromEnvironment()
    {
        var requestedProvider = NormalizeProvider(Environment.GetEnvironmentVariable("ORDERLY_AI_PROVIDER"));
        var timeoutRaw = Environment.GetEnvironmentVariable("ORDERLY_AI_TIMEOUT_SECONDS");
        _ = int.TryParse(timeoutRaw, out var timeoutSeconds);

        var baseUrl = Environment.GetEnvironmentVariable("ORDERLY_AI_BASE_URL");
        var apiKey = Environment.GetEnvironmentVariable("ORDERLY_AI_API_KEY");
        var model = Environment.GetEnvironmentVariable("ORDERLY_AI_MODEL");

        if (string.Equals(requestedProvider, DeepSeekProviderName, StringComparison.Ordinal))
        {
            baseUrl = DeepSeekBaseUrl;
            apiKey = Environment.GetEnvironmentVariable(DeepSeekApiKeyEnvironmentVariableName);

            if (string.IsNullOrWhiteSpace(model))
            {
                model = DeepSeekDefaultModel;
            }
        }

        return new AiProviderOptions(
            requestedProvider,
            baseUrl,
            apiKey,
            model,
            timeoutSeconds);
    }

    public static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return "local";
        }

        return provider.Trim().ToLowerInvariant() switch
        {
            LocalProviderName => LocalProviderName,
            OpenAiCompatibleProviderName => OpenAiCompatibleProviderName,
            DeepSeekProviderName => DeepSeekProviderName,
            _ => provider.Trim().ToLowerInvariant()
        };
    }

    private static int NormalizeTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds < MinTimeoutSeconds)
        {
            return DefaultTimeoutSeconds;
        }

        return Math.Clamp(timeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);
    }
}
