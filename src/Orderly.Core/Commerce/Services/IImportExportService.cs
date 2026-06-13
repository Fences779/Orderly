namespace Orderly.Core.Commerce.Services;

/// <summary>
/// The Universal_Domain_Model entity types that support generic CSV/XLSX import and export
/// (Req 9.1, 9.2). Each value maps to exactly one entity and one deterministic match-key strategy
/// (see <see cref="IImportExportService"/>).
/// </summary>
public enum ImportExportDataType
{
    /// <summary><see cref="Product"/> records. Match key: <c>Code</c> → <c>Name</c>.</summary>
    Product,

    /// <summary><see cref="InventoryItem"/> records. Match key: <c>Sku</c> → <c>Name</c>.</summary>
    InventoryItem,

    /// <summary><see cref="Customer"/> records. Match key: <c>Phone</c> → <c>WeChat</c> → <c>Name</c>.</summary>
    Customer,

    /// <summary><see cref="Order"/> records. Match key: <c>OrderNo</c>.</summary>
    Order,

    /// <summary><see cref="CashFlowEntry"/> records. Match key: <c>ImportBatchId</c> + <c>SourceRowKey</c>.</summary>
    CashFlowEntry,
}

/// <summary>The serialization formats supported for import and export (Req 9.1, 9.2).</summary>
public enum ImportExportFormat
{
    /// <summary>Comma-separated values (RFC 4180 style quoting).</summary>
    Csv,

    /// <summary>Office Open XML spreadsheet (<c>.xlsx</c>).</summary>
    Xlsx,
}

/// <summary>
/// The classification assigned to a single import row by the preview (design Import section).
/// Every row is classified as exactly one of these.
/// </summary>
public enum ImportRowClassification
{
    /// <summary>No existing record matches the row's primary or fallback key; the row will be inserted.</summary>
    Add,

    /// <summary>Exactly one existing record matches deterministically; the row will update it.</summary>
    Update,

    /// <summary>The row fails schema/value validation and will never be written.</summary>
    Error,

    /// <summary>
    /// The deterministic match is ambiguous (a fallback key matches more than one existing record);
    /// the row is reported with <see cref="ImportResultCode.ImportRowConflict"/> and is never silently
    /// updated (Req 9.4).
    /// </summary>
    Conflict,
}

/// <summary>Outcome codes surfaced by the Import_Export_Service (design error-code table, Req 9.3, 9.4, 9.7).</summary>
public enum ImportResultCode
{
    /// <summary>The operation succeeded.</summary>
    Success,

    /// <summary>The file is not a valid CSV/XLSX file or its columns do not match the schema (Req 9.3).</summary>
    ImportRejected,

    /// <summary>One or more preview rows were ambiguous fallback matches classified as Conflict (Req 9.4).</summary>
    ImportRowConflict,

    /// <summary>A commit-level error occurred; the data was rolled back to its pre-commit state (Req 9.7).</summary>
    CommitFailedRolledBack,
}

/// <summary>
/// The preview classification of a single data row, produced before any data is committed (Req 9.4).
/// </summary>
public sealed record ImportRowPreview
{
    /// <summary>1-based position of this data row in the source file (excludes the header).</summary>
    public required int RowNumber { get; init; }

    /// <summary>The row's classification: Add, Update, Error, or Conflict.</summary>
    public required ImportRowClassification Classification { get; init; }

    /// <summary>For an <see cref="ImportRowClassification.Update"/> row, the id of the matched existing record.</summary>
    public Guid? MatchedEntityId { get; init; }

    /// <summary>For an Error or Conflict row, a human-readable reason; otherwise <c>null</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The parsed cell values for this row, keyed by canonical column name. Carried on the preview so
    /// <see cref="IImportExportService.CommitImportAsync"/> can apply the row without re-reading the file.
    /// </summary>
    public required IReadOnlyDictionary<string, string?> Values { get; init; }
}

/// <summary>
/// The result of previewing an import: per-row classification plus the Add/Update/Error/Conflict
/// counts, produced before any data is committed (Req 9.4). When the whole file is rejected
/// (Req 9.3), <see cref="IsRejected"/> is <c>true</c>, <see cref="Code"/> is
/// <see cref="ImportResultCode.ImportRejected"/>, and <see cref="Rows"/> is empty.
/// </summary>
public sealed record ImportPreview
{
    /// <summary>The entity type this preview targets.</summary>
    public required ImportExportDataType DataType { get; init; }

    /// <summary>The source format that was parsed.</summary>
    public required ImportExportFormat Format { get; init; }

    /// <summary>The overall outcome code for the preview.</summary>
    public required ImportResultCode Code { get; init; }

    /// <summary><c>true</c> when the entire file was rejected before classification (Req 9.3).</summary>
    public bool IsRejected => Code == ImportResultCode.ImportRejected;

    /// <summary>When the file is rejected, the reason for rejection; otherwise <c>null</c>.</summary>
    public string? RejectionReason { get; init; }

    /// <summary>Count of rows classified <see cref="ImportRowClassification.Add"/>.</summary>
    public int AddCount { get; init; }

    /// <summary>Count of rows classified <see cref="ImportRowClassification.Update"/>.</summary>
    public int UpdateCount { get; init; }

    /// <summary>Count of rows classified <see cref="ImportRowClassification.Error"/>.</summary>
    public int ErrorCount { get; init; }

    /// <summary>Count of rows classified <see cref="ImportRowClassification.Conflict"/>.</summary>
    public int ConflictCount { get; init; }

    /// <summary>The per-row classifications, in file order.</summary>
    public IReadOnlyList<ImportRowPreview> Rows { get; init; } = Array.Empty<ImportRowPreview>();
}

/// <summary>A single row that could not be committed, with the reason (Req 9.6).</summary>
public sealed record ImportRowFailure
{
    /// <summary>1-based position of the failed data row in the source file.</summary>
    public required int RowNumber { get; init; }

    /// <summary>Human-readable reason the row could not be imported.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// The result of committing an import. On success only the Add/Update rows were applied and
/// pre-existing unreferenced records were left unchanged (Req 9.5); any non-fatal per-row failures
/// are listed in <see cref="Failures"/> (Req 9.6). On a commit-level error the whole import was
/// rolled back to its pre-commit state and <see cref="Code"/> is
/// <see cref="ImportResultCode.CommitFailedRolledBack"/> (Req 9.7).
/// </summary>
public sealed record ImportCommitResult
{
    /// <summary>The overall outcome code for the commit.</summary>
    public required ImportResultCode Code { get; init; }

    /// <summary><c>true</c> when the commit was applied (i.e., not rolled back/rejected).</summary>
    public bool Committed => Code == ImportResultCode.Success;

    /// <summary>Count of records inserted.</summary>
    public int AddedCount { get; init; }

    /// <summary>Count of records updated.</summary>
    public int UpdatedCount { get; init; }

    /// <summary>Rows that could not be imported, each with a reason (Req 9.6).</summary>
    public IReadOnlyList<ImportRowFailure> Failures { get; init; } = Array.Empty<ImportRowFailure>();

    /// <summary>An optional human-readable message describing the outcome.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Generic CSV/XLSX import and export over the Universal_Domain_Model (Req 9). All import and export
/// logic lives inside this service (Req 9.8): file (de)serialization, schema validation, deterministic
/// match-key resolution, preview classification, transactional commit, and rollback.
///
/// <para><b>Export (Req 9.1).</b> <see cref="ExportAsync"/> produces a file in the chosen format
/// containing all active records of the selected type.</para>
///
/// <para><b>Import flow (Req 9.2–9.7).</b> A caller first calls <see cref="PreviewImportAsync"/>:
/// the file is validated (rejected as a whole with <see cref="ImportResultCode.ImportRejected"/> when
/// it is not valid CSV/XLSX or its columns do not match the schema), then every row is classified —
/// using the deterministic per-entity match keys below — as Add, Update, Error, or Conflict, and the
/// counts are returned before anything is written. The caller then passes the preview to
/// <see cref="CommitImportAsync"/>, which applies only the Add and Update rows inside a single
/// Core_Write_Transaction, reports per-row failures, and rolls everything back on a commit-level error.</para>
///
/// <para><b>Deterministic match keys.</b> Matching is resolved per entity in priority order:
/// Product <c>Code</c> → <c>Name</c>; InventoryItem <c>Sku</c> → <c>Name</c>;
/// Customer <c>Phone</c> → <c>WeChat</c> → <c>Name</c>; Order <c>OrderNo</c>;
/// CashFlowEntry <c>ImportBatchId</c> + <c>SourceRowKey</c>. An ambiguous fallback match (a fallback
/// key matching more than one existing record) is classified as <see cref="ImportRowClassification.Conflict"/>
/// and is never silently updated (Req 9.4).</para>
///
/// <para><b>Idempotent commit.</b> Because matching is deterministic, re-importing the same file
/// resolves previously-added rows to Update rather than inserting duplicates.</para>
/// </summary>
public interface IImportExportService
{
    /// <summary>
    /// Exports all active records of <paramref name="dataType"/> in the chosen
    /// <paramref name="format"/> and returns the file bytes (Req 9.1).
    /// </summary>
    Task<byte[]> ExportAsync(
        ImportExportDataType dataType,
        ImportExportFormat format,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and previews an import of <paramref name="content"/> for <paramref name="dataType"/>
    /// without committing anything (Req 9.3, 9.4). Returns a rejected preview when the file is not a
    /// valid CSV/XLSX file or its columns do not match the schema; otherwise returns the per-row
    /// classification and the Add/Update/Error/Conflict counts.
    /// </summary>
    Task<ImportPreview> PreviewImportAsync(
        ImportExportDataType dataType,
        ImportExportFormat format,
        byte[] content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a previously produced <paramref name="preview"/>: applies only its Add and Update rows
    /// inside a single Core_Write_Transaction, leaves unreferenced records unchanged (Req 9.5), reports
    /// per-row failures (Req 9.6), and rolls everything back on a commit-level error returning
    /// <see cref="ImportResultCode.CommitFailedRolledBack"/> (Req 9.7).
    /// </summary>
    Task<ImportCommitResult> CommitImportAsync(
        ImportPreview preview,
        CancellationToken cancellationToken = default);
}
