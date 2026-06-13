using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="IImportExportService"/> (Req 9). All import
/// and export logic lives inside this service and its per-entity handlers (Req 9.8): file
/// (de)serialization, schema validation, deterministic match-key resolution, preview classification,
/// transactional commit, and rollback. The service itself is a thin dispatcher that routes each
/// request to the <see cref="ImportExportHandler{TEntity}"/> for the requested
/// <see cref="ImportExportDataType"/>.
///
/// <para>Industry-agnostic and free of any Forbidden_Term. Reads and writes flow through the Commerce
/// repositories and a single <see cref="CoreWriteTransaction"/>, so the encrypted-connection path and
/// the P0_Security_System (C-2) are preserved.</para>
/// </summary>
public sealed class CommerceImportExportService : IImportExportService
{
    private readonly IReadOnlyDictionary<ImportExportDataType, IImportExportHandler> _handlers;

    /// <summary>
    /// Creates the service over the five import/export-capable Commerce repositories and the SQLite
    /// connection factory used to open the Core_Write_Transaction for commits.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is null.</exception>
    public CommerceImportExportService(
        SqliteConnectionFactory connectionFactory,
        IProductRepository productRepository,
        IInventoryItemRepository inventoryItemRepository,
        ICommerceCustomerRepository customerRepository,
        ICommerceOrderRepository orderRepository,
        ICashFlowEntryRepository cashFlowEntryRepository)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(productRepository);
        ArgumentNullException.ThrowIfNull(inventoryItemRepository);
        ArgumentNullException.ThrowIfNull(customerRepository);
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(cashFlowEntryRepository);

        var handlers = new IImportExportHandler[]
        {
            new ProductImportExportHandler(connectionFactory, productRepository),
            new InventoryItemImportExportHandler(connectionFactory, inventoryItemRepository),
            new CustomerImportExportHandler(connectionFactory, customerRepository),
            new OrderImportExportHandler(connectionFactory, orderRepository),
            new CashFlowEntryImportExportHandler(connectionFactory, cashFlowEntryRepository),
        };

        _handlers = handlers.ToDictionary(handler => handler.DataType);
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportAsync(
        ImportExportDataType dataType,
        ImportExportFormat format,
        CancellationToken cancellationToken = default)
    {
        IImportExportHandler handler = ResolveHandler(dataType);
        IReadOnlyList<IReadOnlyList<string>> rows = await handler.ExportRowsAsync(cancellationToken).ConfigureAwait(false);
        return ImportExportSpreadsheet.Write(ToKind(format), handler.Columns, rows);
    }

    /// <inheritdoc />
    public async Task<ImportPreview> PreviewImportAsync(
        ImportExportDataType dataType,
        ImportExportFormat format,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        IImportExportHandler handler = ResolveHandler(dataType);

        SpreadsheetData data;
        try
        {
            data = ImportExportSpreadsheet.Read(ToKind(format), content);
        }
        catch (SpreadsheetFormatException ex)
        {
            // The file is not valid CSV/XLSX: reject the whole file before any classification (Req 9.3).
            return Rejected(dataType, format, ex.Message);
        }

        // The header must contain every required column for the target schema, else reject (Req 9.3).
        var presentHeaders = new HashSet<string>(data.Header, StringComparer.Ordinal);
        List<string> missing = handler.RequiredHeaderColumns
            .Where(column => !presentHeaders.Contains(column))
            .ToList();
        if (missing.Count > 0)
        {
            return Rejected(
                dataType,
                format,
                $"导入文件的列与所选数据类型的结构不匹配，缺少必需列：{string.Join("、", missing)}。");
        }

        return await handler.ClassifyAsync(format, data.Header, data.Rows, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportCommitResult> CommitImportAsync(
        ImportPreview preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (preview.IsRejected)
        {
            return new ImportCommitResult
            {
                Code = ImportResultCode.ImportRejected,
                Message = preview.RejectionReason ?? "导入文件已被拒绝，无法提交。",
            };
        }

        IImportExportHandler handler = ResolveHandler(preview.DataType);
        return await handler.CommitAsync(preview, cancellationToken).ConfigureAwait(false);
    }

    private IImportExportHandler ResolveHandler(ImportExportDataType dataType)
        => _handlers.TryGetValue(dataType, out IImportExportHandler? handler)
            ? handler
            : throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unsupported import/export data type.");

    private static ImportExportFormatKind ToKind(ImportExportFormat format)
        => format == ImportExportFormat.Csv ? ImportExportFormatKind.Csv : ImportExportFormatKind.Xlsx;

    private static ImportPreview Rejected(ImportExportDataType dataType, ImportExportFormat format, string reason)
        => new()
        {
            DataType = dataType,
            Format = format,
            Code = ImportResultCode.ImportRejected,
            RejectionReason = reason,
        };
}

/// <summary>
/// A per-row failure raised while applying an import row. Caught per row so the remaining valid rows
/// still commit and the failed row is reported (Req 9.6). Distinct from an infrastructure/commit-level
/// failure, which is allowed to propagate so the whole transaction rolls back (Req 9.7).
/// </summary>
internal sealed class ImportRowException : Exception
{
    public ImportRowException(string message) : base(message) { }
}

/// <summary>Non-generic facade over the entity-typed import/export handlers so the service can dispatch by data type.</summary>
internal interface IImportExportHandler
{
    ImportExportDataType DataType { get; }

    IReadOnlyList<string> Columns { get; }

    IReadOnlyList<string> RequiredHeaderColumns { get; }

    Task<IReadOnlyList<IReadOnlyList<string>>> ExportRowsAsync(CancellationToken cancellationToken);

    Task<ImportPreview> ClassifyAsync(
        ImportExportFormat format,
        IReadOnlyList<string> header,
        IReadOnlyList<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken);

    Task<ImportCommitResult> CommitAsync(ImportPreview preview, CancellationToken cancellationToken);
}
