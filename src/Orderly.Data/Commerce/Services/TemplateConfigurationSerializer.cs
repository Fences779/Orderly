using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orderly.Core.Commerce;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Serializes a <see cref="TemplateConfiguration"/> (page configuration + workflow configuration) to
/// and from the JSON stored verbatim in <see cref="BusinessTemplate.ConfigJson"/> (Req 5.5, 5.6).
/// Enums (stage values and element visibility) are written as their string names so the document is
/// stable and human-readable, and the serialization round-trips: deserializing a serialized
/// configuration yields an equivalent configuration.
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term.</para>
/// </summary>
public static class TemplateConfigurationSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Serializes <paramref name="configuration"/> to its JSON representation for storage in
    /// <see cref="BusinessTemplate.ConfigJson"/> (Req 5.5, 5.6).
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is null.</exception>
    public static string Serialize(TemplateConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return JsonSerializer.Serialize(configuration, Options);
    }

    /// <summary>
    /// Deserializes a <see cref="TemplateConfiguration"/> from <paramref name="configJson"/>. A null,
    /// empty, or whitespace payload yields the default (empty) configuration, so a template with no
    /// stored configuration still resolves to defined initial stages and an empty page configuration.
    /// </summary>
    /// <exception cref="JsonException">Thrown when <paramref name="configJson"/> is not valid JSON for the configuration shape.</exception>
    public static TemplateConfiguration Deserialize(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return new TemplateConfiguration();
        }

        return JsonSerializer.Deserialize<TemplateConfiguration>(configJson, Options)
            ?? new TemplateConfiguration();
    }
}
