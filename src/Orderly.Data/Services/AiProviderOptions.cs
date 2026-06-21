using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed class AiProviderOptions
{
    private const int MaxProviderNameCharacters = 64;
    private const int MaxBaseUrlCharacters = 2048;
    private const int MaxApiKeyCharacters = 4096;
    private const int MaxModelCharacters = 256;

    public const string LocalProviderName = "local";
    public const string OpenAiCompatibleProviderName = "openai-compatible";
    public const string DeepSeekProviderName = "deepseek";
    public const string DeepSeekBaseUrl = "https://api.deepseek.com";
    public const string DeepSeekDefaultModel = "deepseek-chat";
    public const string DeepSeekApiKeyEnvironmentVariableName = "DEEPSEEK_API_KEY";
    public const string AllowedHostsEnvironmentVariableName = "ORDERLY_AI_ALLOWED_HOSTS";
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
        BaseUrl = NormalizeConfigurationValue("ORDERLY_AI_BASE_URL", baseUrl, MaxBaseUrlCharacters);
        ApiKey = NormalizeConfigurationValue("AI provider API key", apiKey, MaxApiKeyCharacters);
        Model = NormalizeConfigurationValue("ORDERLY_AI_MODEL", model, MaxModelCharacters);
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
        var normalized = NormalizeConfigurationValue("ORDERLY_AI_PROVIDER", provider, MaxProviderNameCharacters);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "local";
        }

        return normalized.ToLowerInvariant() switch
        {
            LocalProviderName => LocalProviderName,
            OpenAiCompatibleProviderName => OpenAiCompatibleProviderName,
            DeepSeekProviderName => DeepSeekProviderName,
            _ => normalized.ToLowerInvariant()
        };
    }

    public AiProviderOptions WithPreferences(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var model = string.IsNullOrWhiteSpace(preferences.AiDefaultModel)
            ? Model
            : preferences.AiDefaultModel;

        return new AiProviderOptions(
            RequestedProvider,
            BaseUrl,
            ApiKey,
            model,
            preferences.AiTimeoutSeconds);
    }

    private static int NormalizeTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds < MinTimeoutSeconds)
        {
            return DefaultTimeoutSeconds;
        }

        return Math.Clamp(timeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);
    }

    private static string NormalizeConfigurationValue(string configurationName, string? value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{configurationName} is too long.");
        }

        if (normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException($"{configurationName} contains invalid control characters.");
        }

        return normalized;
    }
}
