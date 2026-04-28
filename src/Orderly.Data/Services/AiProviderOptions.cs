namespace Orderly.Data.Services;

public sealed class AiProviderOptions
{
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
        var timeoutRaw = Environment.GetEnvironmentVariable("ORDERLY_AI_TIMEOUT_SECONDS");
        _ = int.TryParse(timeoutRaw, out var timeoutSeconds);

        return new AiProviderOptions(
            Environment.GetEnvironmentVariable("ORDERLY_AI_PROVIDER"),
            Environment.GetEnvironmentVariable("ORDERLY_AI_BASE_URL"),
            Environment.GetEnvironmentVariable("ORDERLY_AI_API_KEY"),
            Environment.GetEnvironmentVariable("ORDERLY_AI_MODEL"),
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
            "local" => "local",
            "openai-compatible" => "openai-compatible",
            _ => provider.Trim().ToLowerInvariant()
        };
    }

    private static int NormalizeTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds <= 0)
        {
            return DefaultTimeoutSeconds;
        }

        return Math.Clamp(timeoutSeconds, 1, DefaultTimeoutSeconds);
    }
}
