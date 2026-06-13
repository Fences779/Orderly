namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Stable identity of the single built-in Business_Template (Req 5.3). Developer code and documentation
/// MAY refer to the template by its internal key <see cref="Key"/>; the user-facing UI displays only the
/// Simplified Chinese <see cref="DisplayName"/> (Req 5.8).
/// </summary>
public static class BuiltInBusinessTemplate
{
    /// <summary>The internal stable key of the single built-in template (Req 5.3).</summary>
    public const string Key = "DefaultCommerce";

    /// <summary>The user-visible Simplified Chinese display name of the built-in template (Req 5.3, 5.8).</summary>
    public const string DisplayName = "默认经营模板";
}

/// <summary>The outcome of an attempted JSON template import (Req 5.1, 5.2).</summary>
public enum TemplateImportOutcome
{
    /// <summary>The payload was valid and a new Business_Template was created (Req 5.1).</summary>
    Imported = 0,

    /// <summary>
    /// The JSON payload failed schema validation or referenced an undefined Universal_Domain_Model
    /// entity type; the import was rejected and all existing Business_Templates were left unchanged
    /// (Req 5.2).
    /// </summary>
    TemplateImportInvalid = 1
}

/// <summary>
/// The result of <see cref="IBusinessTemplateService.ImportAsync"/>. Distinguishes a successful import
/// from a rejected one (Req 5.1, 5.2), following the typed-result convention used across the Commerce
/// Service Layer.
/// </summary>
public sealed record TemplateImportResult
{
    /// <summary>The outcome of the import attempt.</summary>
    public TemplateImportOutcome Outcome { get; init; }

    /// <summary>
    /// The template that was created on a successful import; <c>null</c> when the import was rejected.
    /// </summary>
    public BusinessTemplate? Template { get; init; }

    /// <summary>
    /// A neutral, human-readable explanation of the specific validation failure when the import was
    /// rejected (Req 5.2); <c>null</c> on success.
    /// </summary>
    public string? Error { get; init; }

    /// <summary><c>true</c> when the payload was valid and a template was created.</summary>
    public bool IsImported => Outcome == TemplateImportOutcome.Imported;

    /// <summary>
    /// <c>true</c> when the import was rejected; in this case all existing Business_Templates are
    /// unchanged (Req 5.2).
    /// </summary>
    public bool IsInvalid => Outcome == TemplateImportOutcome.TemplateImportInvalid;

    /// <summary>Creates the canonical "imported" result (Req 5.1).</summary>
    public static TemplateImportResult Imported(BusinessTemplate template) => new()
    {
        Outcome = TemplateImportOutcome.Imported,
        Template = template
    };

    /// <summary>Creates the canonical "import invalid" result (Req 5.2).</summary>
    public static TemplateImportResult Invalid(string error) => new()
    {
        Outcome = TemplateImportOutcome.TemplateImportInvalid,
        Error = error
    };
}

/// <summary>
/// Commerce Service Layer contract for <see cref="BusinessTemplate"/> operations over the
/// Universal_Domain_Model (Req 4.1, 5.1–5.3, 5.7, 5.8). The service supports create, edit, activate,
/// clone, import, and export of templates using JSON as the import/export serialization format
/// (Req 5.1), provides exactly one built-in template (key <see cref="BuiltInBusinessTemplate.Key"/>,
/// display name <see cref="BuiltInBusinessTemplate.DisplayName"/>) (Req 5.3), and resolves a workspace
/// with no explicitly activated template to that built-in template (Req 5.7).
///
/// <para>An import that fails schema validation or references an undefined Universal_Domain_Model
/// entity type is rejected with <see cref="TemplateImportOutcome.TemplateImportInvalid"/> and leaves
/// all existing templates unchanged (Req 5.2).</para>
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public interface IBusinessTemplateService
{
    /// <summary>
    /// Creates and persists a new workspace-owned <see cref="BusinessTemplate"/> (Req 5.1).
    /// </summary>
    /// <param name="templateKey">The stable internal key for the template. Must be non-empty.</param>
    /// <param name="displayName">The user-visible display name. Must be non-empty (Req 5.8).</param>
    /// <param name="configJson">Optional page/workflow configuration JSON payload, or <c>null</c>.</param>
    /// <param name="workspaceId">The owning workspace identity for the new template.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="templateKey"/> or <paramref name="displayName"/> is null/empty.</exception>
    Task<BusinessTemplate> CreateAsync(
        string templateKey,
        string displayName,
        string? configJson,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    /// <summary>Persists an edit to an existing template's display name and configuration (Req 5.1).</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="template"/> is null.</exception>
    Task UpdateAsync(BusinessTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Returns the active (non-soft-deleted) template with the given id, or <c>null</c> when none.</summary>
    Task<BusinessTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all active templates (built-in and workspace-owned).</summary>
    Task<IReadOnlyList<BusinessTemplate>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the single built-in template (key <see cref="BuiltInBusinessTemplate.Key"/>),
    /// provisioning it on first request if it does not yet exist, so that exactly one built-in
    /// template ever exists (Req 5.3).
    /// </summary>
    Task<BusinessTemplate> GetOrCreateBuiltInTemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly activates the template identified by <paramref name="templateId"/> as the active
    /// template of the workspace identified by <paramref name="workspaceId"/> (Req 5.1).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the workspace or template does not exist.</exception>
    Task ActivateAsync(Guid workspaceId, Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the active template for the workspace identified by <paramref name="workspaceId"/>.
    /// When the workspace has no explicitly activated template (or the referenced template no longer
    /// exists), the built-in <see cref="BuiltInBusinessTemplate.Key"/> template is returned (Req 5.7).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the workspace does not exist.</exception>
    Task<BusinessTemplate> GetActiveTemplateAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clones the template identified by <paramref name="sourceTemplateId"/> into a new workspace-owned
    /// template for <paramref name="workspaceId"/>, preserving its configuration (Req 5.1).
    /// </summary>
    /// <param name="sourceTemplateId">The template to clone.</param>
    /// <param name="workspaceId">The owning workspace for the cloned template.</param>
    /// <param name="newDisplayName">The display name for the clone. Must be non-empty (Req 5.8).</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="newDisplayName"/> is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the source template does not exist.</exception>
    Task<BusinessTemplate> CloneAsync(
        Guid sourceTemplateId,
        Guid workspaceId,
        string newDisplayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Serializes <paramref name="template"/> to its JSON export representation (Req 5.1). The result
    /// is symmetric with <see cref="ImportAsync"/>: importing the produced JSON yields an equivalent
    /// template (Property 15).
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="template"/> is null.</exception>
    string Export(BusinessTemplate template);

    /// <summary>Loads the template with the given id and serializes it to its JSON export representation (Req 5.1).</summary>
    /// <exception cref="InvalidOperationException">Thrown when the template does not exist.</exception>
    Task<string> ExportAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a template from its JSON representation into a new workspace-owned template (Req 5.1).
    /// When the payload fails schema validation or references an undefined Universal_Domain_Model
    /// entity type, the import is rejected with <see cref="TemplateImportOutcome.TemplateImportInvalid"/>,
    /// an error describing the specific failure is returned, and all existing templates are left
    /// unchanged (Req 5.2).
    /// </summary>
    /// <param name="json">The JSON payload to import.</param>
    /// <param name="workspaceId">The owning workspace for the imported template on success.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    Task<TemplateImportResult> ImportAsync(
        string json,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
}
