using System.Globalization;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Generic base for the per-entity import/export handlers. It implements the parts of the import
/// flow that are identical for every entity — export-row projection, preview classification using the
/// deterministic per-entity match keys, and the transactional commit — while concrete handlers supply
/// the entity-specific schema (columns, required columns), the ordered match-key extractors, row
/// validation, and the add/update field mapping.
///
/// <para><b>Deterministic matching (design Import section, Req 9.4).</b> Match keys are evaluated in
/// priority order. For a row, the highest-priority key whose value is present (non-blank) is the
/// <i>chosen</i> level; the row is matched against existing active records on that same field. Zero
/// matches → <see cref="ImportRowClassification.Add"/>; exactly one → <see cref="ImportRowClassification.Update"/>;
/// more than one → <see cref="ImportRowClassification.Conflict"/> (ambiguous fallback match, never
/// silently updated). A row that fails validation is <see cref="ImportRowClassification.Error"/>.</para>
/// </summary>
internal abstract class ImportExportHandler<TEntity> : IImportExportHandler
    where TEntity : CommerceEntity
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ICommerceRepository<TEntity> _repository;

    protected ImportExportHandler(SqliteConnectionFactory connectionFactory, ICommerceRepository<TEntity> repository)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public abstract ImportExportDataType DataType { get; }

    public abstract IReadOnlyList<string> Columns { get; }

    public abstract IReadOnlyList<string> RequiredHeaderColumns { get; }

    /// <summary>The ordered match-key extractors over a parsed row, highest priority first.</summary>
    protected abstract IReadOnlyList<Func<RowValues, string?>> RowKeyExtractors { get; }

    /// <summary>The ordered match-key extractors over an existing entity, aligned 1:1 with <see cref="RowKeyExtractors"/>.</summary>
    protected abstract IReadOnlyList<Func<TEntity, string?>> EntityKeyExtractors { get; }

    /// <summary>Projects an entity to its cell values aligned to <see cref="Columns"/> (export).</summary>
    protected abstract IReadOnlyList<string> ToCells(TEntity entity);

    /// <summary>Validates a parsed row; returns a human-readable reason when invalid, or null when valid.</summary>
    protected abstract string? ValidateRow(RowValues values);

    /// <summary>Builds a new entity from a parsed row (an <see cref="ImportRowClassification.Add"/> row).</summary>
    /// <exception cref="ImportRowException">Thrown when the row cannot produce a valid entity (per-row failure).</exception>
    protected abstract TEntity BuildEntity(RowValues values);

    /// <summary>Applies a parsed row's values onto an existing entity (an <see cref="ImportRowClassification.Update"/> row).</summary>
    /// <exception cref="ImportRowException">Thrown when the row cannot be applied (per-row failure).</exception>
    protected abstract void ApplyUpdate(TEntity existing, RowValues values);

    public async Task<IReadOnlyList<IReadOnlyList<string>>> ExportRowsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TEntity> entities = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<IReadOnlyList<string>>(entities.Count);
        foreach (TEntity entity in entities)
        {
            rows.Add(ToCells(entity));
        }

        return rows;
    }

    public async Task<ImportPreview> ClassifyAsync(
        ImportExportFormat format,
        IReadOnlyList<string> header,
        IReadOnlyList<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken)
    {
        Dictionary<string, int> headerIndex = BuildHeaderIndex(header);
        IReadOnlyList<TEntity> existing = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var previews = new List<ImportRowPreview>(rows.Count);
        int add = 0, update = 0, error = 0, conflict = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            int rowNumber = i + 1;
            RowValues values = BuildRowValues(headerIndex, rows[i]);

            string? validationError = ValidateRow(values);
            if (validationError is not null)
            {
                error++;
                previews.Add(new ImportRowPreview
                {
                    RowNumber = rowNumber,
                    Classification = ImportRowClassification.Error,
                    Reason = validationError,
                    Values = values.Snapshot,
                });
                continue;
            }

            (ImportRowClassification classification, Guid? matchedId, string? reason) = Classify(values, existing);
            switch (classification)
            {
                case ImportRowClassification.Add: add++; break;
                case ImportRowClassification.Update: update++; break;
                case ImportRowClassification.Conflict: conflict++; break;
                default: error++; break;
            }

            previews.Add(new ImportRowPreview
            {
                RowNumber = rowNumber,
                Classification = classification,
                MatchedEntityId = matchedId,
                Reason = reason,
                Values = values.Snapshot,
            });
        }

        return new ImportPreview
        {
            DataType = DataType,
            Format = format,
            Code = conflict > 0 ? ImportResultCode.ImportRowConflict : ImportResultCode.Success,
            AddCount = add,
            UpdateCount = update,
            ErrorCount = error,
            ConflictCount = conflict,
            Rows = previews,
        };
    }

    public async Task<ImportCommitResult> CommitAsync(ImportPreview preview, CancellationToken cancellationToken)
    {
        var failures = new List<ImportRowFailure>();
        int added = 0;
        int updated = 0;

        // A single Core_Write_Transaction so the whole commit is all-or-nothing on an infrastructure
        // failure; row-level failures are captured and the remaining rows still commit (Req 9.6, 9.7).
        using CoreWriteTransaction transaction = CoreWriteTransaction.Begin(_connectionFactory);
        try
        {
            foreach (ImportRowPreview row in preview.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = RowValues.FromSnapshot(row.Values);

                if (row.Classification == ImportRowClassification.Add)
                {
                    try
                    {
                        TEntity entity = BuildEntity(values);
                        await _repository.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
                        added++;
                    }
                    catch (ImportRowException ex)
                    {
                        failures.Add(new ImportRowFailure { RowNumber = row.RowNumber, Reason = ex.Message });
                    }
                }
                else if (row.Classification == ImportRowClassification.Update)
                {
                    try
                    {
                        TEntity? existing = row.MatchedEntityId is Guid id
                            ? await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                            : null;
                        if (existing is null)
                        {
                            failures.Add(new ImportRowFailure
                            {
                                RowNumber = row.RowNumber,
                                Reason = "待更新的记录不存在或已被删除。",
                            });
                            continue;
                        }

                        ApplyUpdate(existing, values);
                        await _repository.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
                        updated++;
                    }
                    catch (ImportRowException ex)
                    {
                        failures.Add(new ImportRowFailure { RowNumber = row.RowNumber, Reason = ex.Message });
                    }
                }

                // Error and Conflict rows are never written (design Import section).
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A commit-level error: disposal rolls the whole transaction back to its pre-commit state
            // and we report CommitFailedRolledBack (Req 9.7).
            return new ImportCommitResult
            {
                Code = ImportResultCode.CommitFailedRolledBack,
                Message = "提交过程中发生错误，所有更改已回滚到提交前的状态。",
            };
        }

        return new ImportCommitResult
        {
            Code = ImportResultCode.Success,
            AddedCount = added,
            UpdatedCount = updated,
            Failures = failures,
        };
    }

    /// <summary>
    /// Resolves a validated row to a classification using the deterministic match keys. The first
    /// key level whose row value is present is chosen; the row is matched against existing records on
    /// that field. No present key value at any level → treated as an unmatchable Error.
    /// </summary>
    private (ImportRowClassification Classification, Guid? MatchedId, string? Reason) Classify(
        RowValues values,
        IReadOnlyList<TEntity> existing)
    {
        for (int level = 0; level < RowKeyExtractors.Count; level++)
        {
            string? rowKey = Normalize(RowKeyExtractors[level](values));
            if (rowKey is null)
            {
                continue; // Fall back to the next-priority key when this one is blank.
            }

            Func<TEntity, string?> entityKey = EntityKeyExtractors[level];
            var matches = existing
                .Where(entity => string.Equals(Normalize(entityKey(entity)), rowKey, StringComparison.Ordinal))
                .Select(entity => entity.Id)
                .ToList();

            if (matches.Count == 0)
            {
                return (ImportRowClassification.Add, null, null);
            }

            if (matches.Count == 1)
            {
                return (ImportRowClassification.Update, matches[0], null);
            }

            return (
                ImportRowClassification.Conflict,
                null,
                $"匹配键“{rowKey}”对应到多条已存在的记录，存在歧义，不会自动更新。");
        }

        return (ImportRowClassification.Error, null, "无法确定用于匹配的关键字段。");
    }

    private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> header)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            string name = header[i];
            // First occurrence wins for duplicated headers.
            if (!index.ContainsKey(name))
            {
                index[name] = i;
            }
        }

        return index;
    }

    private RowValues BuildRowValues(IReadOnlyDictionary<string, int> headerIndex, IReadOnlyList<string> cells)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string column in Columns)
        {
            if (headerIndex.TryGetValue(column, out int idx) && idx < cells.Count)
            {
                map[column] = cells[idx];
            }
        }

        return new RowValues(map);
    }

    /// <summary>Trims a key/comparison value and maps blank to null so blank keys fall through to the next priority.</summary>
    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ----- Shared parse/format helpers for concrete handlers -----

    protected static string Text(CommerceMoney money) => money.Amount.ToString("0.00", CultureInfo.InvariantCulture);

    protected static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    protected static string Text(Guid value) => value.ToString();

    protected static string Text(Guid? value) => value?.ToString() ?? string.Empty;

    protected static string Text(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    protected static string Text(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    protected static string Text<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToString();

    protected static bool TryParseMoney(string? raw, out CommerceMoney money)
    {
        money = CommerceMoney.Zero;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true; // Treat a blank money cell as 0.00.
        }

        return decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            && CommerceMoney.TryFrom(value, out money);
    }

    protected static bool TryParseDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true; // Treat a blank numeric cell as 0.
        }

        return decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    protected static bool TryParseGuid(string? raw, out Guid value)
        => Guid.TryParse((raw ?? string.Empty).Trim(), out value);

    protected static bool TryParseGuidOptional(string? raw, out Guid? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (Guid.TryParse(raw.Trim(), out Guid parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    protected static bool TryParseDateTime(string? raw, out DateTime value)
        => DateTime.TryParse(
            (raw ?? string.Empty).Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);

    protected static bool TryParseDateTimeOptional(string? raw, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (TryParseDateTime(raw, out DateTime parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    protected static bool TryParseEnum<TEnum>(string? raw, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true; // Blank means "leave default / unspecified".
        }

        return Enum.TryParse(raw.Trim(), ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    protected Guid RequireWorkspaceId(RowValues values)
    {
        if (!TryParseGuid(values.Get("WorkspaceId"), out Guid workspaceId) || workspaceId == Guid.Empty)
        {
            throw new ImportRowException("新增记录缺少有效的 WorkspaceId。");
        }

        return workspaceId;
    }
}

/// <summary>
/// The parsed cell values of one import row, keyed by canonical column name. Carries an immutable
/// snapshot used both for classification and, later, for commit (so the file is read only once).
/// </summary>
internal sealed class RowValues
{
    private readonly IReadOnlyDictionary<string, string?> _values;

    public RowValues(IReadOnlyDictionary<string, string?> values) => _values = values;

    /// <summary>An immutable view of the row's values for placing on the preview.</summary>
    public IReadOnlyDictionary<string, string?> Snapshot => _values;

    /// <summary>The raw value for a column, or null when the column is absent from the file.</summary>
    public string? Get(string column) => _values.TryGetValue(column, out string? value) ? value : null;

    /// <summary>The trimmed value for a column, or null when absent or blank.</summary>
    public string? GetTrimmed(string column)
    {
        string? value = Get(column);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary><c>true</c> when the column is present in the file (even if blank).</summary>
    public bool Has(string column) => _values.ContainsKey(column);

    public static RowValues FromSnapshot(IReadOnlyDictionary<string, string?> snapshot)
        => new(new Dictionary<string, string?>(snapshot, StringComparer.Ordinal));
}
