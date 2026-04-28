namespace Orderly.Core.Models;

public sealed class AiSuggestionProviderResult
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SuggestionText { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;
}
