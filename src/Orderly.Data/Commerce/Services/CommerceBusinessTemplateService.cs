using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="IBusinessTemplateService"/> over the
/// Universal_Domain_Model (Req 4.1, 5.1–5.3, 5.7, 5.8). Supports create/edit/activate/clone and
/// JSON import/export of <see cref="BusinessTemplate"/> records, provisions the single built-in
/// template (key <see cref="BuiltInBusinessTemplate.Key"/>, display name
/// <see cref="BuiltInBusinessTemplate.DisplayName"/>) on demand (Req 5.3), and resolves a workspace
/// with no explicitly activated template to that built-in template (Req 5.7).
///
/// <para><b>JSON document shape (Req 5.1).</b> A template serializes to a JSON object:
/// <c>{ "schemaVersion": 1, "templateKey": "…", "displayName": "…", "config": { … } }</c>, where
/// <c>config</c> carries the template's page/workflow configuration verbatim (it is what is stored in
/// <see cref="BusinessTemplate.ConfigJson"/>). Export and import are symmetric so a round-trip yields
/// an equivalent template (Property 15).</para>
///
/// <para><b>Import validation (Req 5.2).</b> An import is rejected — leaving every existing template
/// unchanged because validation runs entirely before any write — when the payload is not well-formed
/// JSON, is not a JSON object, declares an unsupported <c>schemaVersion</c>, is missing a non-empty
/// <c>templateKey</c> or <c>displayName</c>, has a non-object <c>config</c>, or references an
/// undefined Universal_Domain_Model entity type anywhere in <c>config</c> (any property named
/// <c>entityType</c> or <c>targetEntityType</c> whose string value is not a
/// <see cref="BusinessEntityType"/> name). Rejections return
/// <see cref="TemplateImportOutcome.TemplateImportInvalid"/> with a specific error message.</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads/writes only through
/// the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceBusinessTemplateService : IBusinessTemplateService
{
    /// <summary>The only schema version the import/export format currently supports.</summary>
    private const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly IBusinessTemplateRepository _templateRepository;
    private readonly IBusinessWorkspaceRepository _workspaceRepository;

    /// <summary>Creates the service over the Commerce template and workspace repositories.</summary>
    /// <exception cref="ArgumentNullException">Thrown when a repository is null.</exception>
    public CommerceBusinessTemplateService(
        IBusinessTemplateRepository templateRepository,
        IBusinessWorkspaceRepository workspaceRepository)
    {
        _templateRepository = templateRepository ?? throw new ArgumentNullException(nameof(templateRepository));
        _workspaceRepository = workspaceRepository ?? throw new ArgumentNullException(nameof(workspaceRepository));
    }

    /// <inheritdoc />
    public async Task<BusinessTemplate> CreateAsync(
        string templateKey,
        string displayName,
        string? configJson,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
        {
            throw new ArgumentException("Template key must be a non-empty value.", nameof(templateKey));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Template display name must be a non-empty value.", nameof(displayName));
        }

        var template = new BusinessTemplate
        {
            TemplateKey = templateKey,
            WorkspaceId = workspaceId,
            IsBuiltIn = false,
            DisplayName = displayName,
            ConfigJson = configJson
        };

        return await _templateRepository.CreateAsync(template, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(BusinessTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        await _templateRepository.UpdateAsync(template, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<BusinessTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _templateRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<BusinessTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
        => _templateRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<BusinessTemplate> GetOrCreateBuiltInTemplateAsync(CancellationToken cancellationToken = default)
    {
        BusinessTemplate? existing = await FindBuiltInTemplateAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var builtIn = new BusinessTemplate
        {
            TemplateKey = BuiltInBusinessTemplate.Key,
            WorkspaceId = null,
            IsBuiltIn = true,
            DisplayName = BuiltInBusinessTemplate.DisplayName,
            ConfigJson = null
        };

        return await _templateRepository.CreateAsync(builtIn, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ActivateAsync(Guid workspaceId, Guid templateId, CancellationToken cancellationToken = default)
    {
        BusinessWorkspace workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");

        BusinessTemplate _ = await _templateRepository.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Template '{templateId}' was not found.");

        workspace.ActiveTemplateId = templateId;
        await _workspaceRepository.UpdateAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BusinessTemplate> GetActiveTemplateAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        BusinessWorkspace workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");

        if (workspace.ActiveTemplateId is Guid activeId)
        {
            BusinessTemplate? active = await _templateRepository.GetByIdAsync(activeId, cancellationToken).ConfigureAwait(false);
            if (active is not null)
            {
                return active;
            }
        }

        // No explicitly activated template (or it no longer exists) -> built-in DefaultCommerce (Req 5.7).
        return await GetOrCreateBuiltInTemplateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BusinessTemplate> CloneAsync(
        Guid sourceTemplateId,
        Guid workspaceId,
        string newDisplayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newDisplayName))
        {
            throw new ArgumentException("Template display name must be a non-empty value.", nameof(newDisplayName));
        }

        BusinessTemplate source = await _templateRepository.GetByIdAsync(sourceTemplateId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Template '{sourceTemplateId}' was not found.");

        var clone = new BusinessTemplate
        {
            TemplateKey = $"{source.TemplateKey}-clone-{Guid.NewGuid():N}",
            WorkspaceId = workspaceId,
            IsBuiltIn = false,
            DisplayName = newDisplayName,
            ConfigJson = source.ConfigJson
        };

        return await _templateRepository.CreateAsync(clone, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string Export(BusinessTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var document = new JsonObject
        {
            ["schemaVersion"] = SupportedSchemaVersion,
            ["templateKey"] = template.TemplateKey,
            ["displayName"] = template.DisplayName,
            ["config"] = template.ConfigJson is null ? null : JsonNode.Parse(template.ConfigJson)
        };

        return document.ToJsonString(SerializerOptions);
    }

    /// <inheritdoc />
    public async Task<string> ExportAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        BusinessTemplate template = await _templateRepository.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Template '{templateId}' was not found.");

        return Export(template);
    }

    /// <inheritdoc />
    public async Task<TemplateImportResult> ImportAsync(
        string json,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        // Validate the entire payload before any write so a rejection leaves existing templates unchanged (Req 5.2).
        if (string.IsNullOrWhiteSpace(json))
        {
            return TemplateImportResult.Invalid("导入失败：模板内容为空。"); // "Import failed: the template content is empty."
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return TemplateImportResult.Invalid("导入失败：内容不是有效的 JSON。"); // "Import failed: the content is not valid JSON."
        }

        if (root is not JsonObject document)
        {
            return TemplateImportResult.Invalid("导入失败：模板根节点必须是 JSON 对象。"); // "Import failed: the template root must be a JSON object."
        }

        if (!TryGetSupportedSchemaVersion(document, out string? schemaError))
        {
            return TemplateImportResult.Invalid(schemaError!);
        }

        if (!TryGetRequiredString(document, "templateKey", out string templateKey))
        {
            return TemplateImportResult.Invalid("导入失败：缺少有效的模板键（templateKey）。"); // "Import failed: missing a valid templateKey."
        }

        if (!TryGetRequiredString(document, "displayName", out string displayName))
        {
            return TemplateImportResult.Invalid("导入失败：缺少有效的显示名称（displayName）。"); // "Import failed: missing a valid displayName."
        }

        JsonNode? configNode = document["config"];
        if (configNode is not null and not JsonObject)
        {
            return TemplateImportResult.Invalid("导入失败：配置（config）必须是 JSON 对象。"); // "Import failed: config must be a JSON object."
        }

        if (FindUndefinedEntityType(configNode) is string undefinedEntityType)
        {
            // "Import failed: references an undefined entity type '{0}'."
            return TemplateImportResult.Invalid($"导入失败：引用了未定义的实体类型“{undefinedEntityType}”。");
        }

        string? configJson = configNode?.ToJsonString(SerializerOptions);

        var template = new BusinessTemplate
        {
            TemplateKey = templateKey,
            WorkspaceId = workspaceId,
            IsBuiltIn = false,
            DisplayName = displayName,
            ConfigJson = configJson
        };

        BusinessTemplate created = await _templateRepository.CreateAsync(template, cancellationToken).ConfigureAwait(false);
        return TemplateImportResult.Imported(created);
    }

    /// <summary>Returns the existing built-in template, or <c>null</c> when it has not yet been provisioned.</summary>
    private async Task<BusinessTemplate?> FindBuiltInTemplateAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<BusinessTemplate> all = await _templateRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(t => t.IsBuiltIn && t.TemplateKey == BuiltInBusinessTemplate.Key);
    }

    /// <summary>Validates the required <c>schemaVersion</c> field against the supported version (Req 5.2).</summary>
    private static bool TryGetSupportedSchemaVersion(JsonObject document, out string? error)
    {
        JsonNode? versionNode = document["schemaVersion"];
        if (versionNode is null || versionNode.GetValueKind() != JsonValueKind.Number || !versionNode.AsValue().TryGetValue(out int version))
        {
            error = "导入失败：缺少有效的架构版本（schemaVersion）。"; // "Import failed: missing a valid schemaVersion."
            return false;
        }

        if (version != SupportedSchemaVersion)
        {
            // "Import failed: unsupported schemaVersion '{0}'."
            error = $"导入失败：不支持的架构版本“{version}”。";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Reads a required non-empty string field from the document (Req 5.2).</summary>
    private static bool TryGetRequiredString(JsonObject document, string propertyName, out string value)
    {
        JsonNode? node = document[propertyName];
        if (node is not null && node.GetValueKind() == JsonValueKind.String)
        {
            string? raw = node.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                value = raw;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Walks the configuration JSON tree and returns the first referenced entity-type name that is not
    /// a defined <see cref="BusinessEntityType"/>, or <c>null</c> when every referenced entity type is
    /// defined (Req 5.2). An entity-type reference is any property named <c>entityType</c> or
    /// <c>targetEntityType</c> (case-insensitive).
    /// </summary>
    private static string? FindUndefinedEntityType(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    if (IsEntityTypeProperty(property.Key))
                    {
                        string? referenced = property.Value?.GetValueKind() == JsonValueKind.String
                            ? property.Value!.GetValue<string>()
                            : null;
                        if (!IsDefinedEntityType(referenced))
                        {
                            return referenced ?? string.Empty;
                        }
                    }

                    string? nested = FindUndefinedEntityType(property.Value);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            case JsonArray array:
                foreach (JsonNode? element in array)
                {
                    string? nested = FindUndefinedEntityType(element);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static bool IsEntityTypeProperty(string propertyName)
        => string.Equals(propertyName, "entityType", StringComparison.OrdinalIgnoreCase)
            || string.Equals(propertyName, "targetEntityType", StringComparison.OrdinalIgnoreCase);

    private static bool IsDefinedEntityType(string? value)
        => !string.IsNullOrEmpty(value) && Enum.TryParse<BusinessEntityType>(value, ignoreCase: false, out _);
}
