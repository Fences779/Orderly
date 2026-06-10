namespace Orderly.Data.Services;

internal static class GatewayConfigurationSafety
{
    public const int MaxEndpointCharacters = 2048;
    public const int MaxTokenCharacters = 4096;
    public const int MaxIdentifierCharacters = 128;

    public static string NormalizeOptionalValue(string configurationName, string? value, int maxCharacters)
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

    public static string NormalizeValueOrFallback(string configurationName, string? value, string fallback, int maxCharacters)
    {
        var normalized = NormalizeOptionalValue(configurationName, value, maxCharacters);
        return string.IsNullOrWhiteSpace(normalized)
            ? NormalizeOptionalValue(configurationName, fallback, maxCharacters)
            : normalized;
    }
}
