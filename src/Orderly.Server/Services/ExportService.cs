using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
using Orderly.Server.Data;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface IExportService
{
    Task<CloudExportJobDto> CreateJobAsync(Guid workspaceId, Guid userId, string scope, string clientRequestId, CancellationToken cancellationToken = default);
    Task<CloudExportJobDto?> GetJobAsync(Guid workspaceId, Guid exportId, CancellationToken cancellationToken = default);
    Task RecordDownloadAsync(Guid workspaceId, Guid exportId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task ProcessPendingJobsAsync(CancellationToken cancellationToken = default);
}

public sealed class ExportService : IExportService
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly IBlobStorage _blobStorage;
    private readonly ServerOptions _options;
    private readonly IAuditLogService _auditLogService;
    private readonly IIdempotencyService _idempotencyService;

    public ExportService(
        PostgresConnectionFactory connectionFactory,
        IBlobStorage blobStorage,
        ServerOptions options,
        IAuditLogService auditLogService,
        IIdempotencyService idempotencyService)
    {
        _connectionFactory = connectionFactory;
        _blobStorage = blobStorage;
        _options = options;
        _auditLogService = auditLogService;
        _idempotencyService = idempotencyService;
    }

    public async Task<CloudExportJobDto> CreateJobAsync(Guid workspaceId, Guid userId, string scope, string clientRequestId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId))
        {
            throw new InvalidOperationException("Idempotency key is required.");
        }

        await CleanupExpiredExportsAsync(cancellationToken);
        EnsureLocalExportCapacity(extraBytes: 0);

        var job = new CloudExportJobDto
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            RequestedByUserId = userId,
            Scope = scope,
            Status = EmergencyDraftStatus.Pending
        };

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var requestHash = ComputeRequestHash(new { scope });
        var idempotency = await _idempotencyService.TryBeginAsync(workspaceId, userId, "export:create", clientRequestId, requestHash, connection, transaction, cancellationToken);
        if (!idempotency.ShouldExecute)
        {
            await transaction.CommitAsync(cancellationToken);
            return JsonSerializer.Deserialize<CloudExportJobDto>(idempotency.ResponseBodyJson ?? string.Empty)
                ?? throw new InvalidOperationException("Idempotency replay could not be deserialized.");
        }

        const string sql = @"
            INSERT INTO ""CloudExportJobs"" (
                ""Id"", ""WorkspaceId"", ""RequestedByUserId"", ""Scope"", ""Status"",
                ""FileName"", ""FilePath"", ""ErrorMessage"", ""AttemptCount"", ""LastAttemptAt"",
                ""CreatedAt"", ""CompletedAt"")
            VALUES (
                @Id, @WorkspaceId, @RequestedByUserId, @Scope, @Status,
                @FileName, @FilePath, @ErrorMessage, @AttemptCount, @LastAttemptAt,
                @CreatedAt, @CompletedAt);";

        await connection.ExecuteAsync(sql, new
        {
            job.Id,
            job.WorkspaceId,
            job.RequestedByUserId,
            job.Scope,
            job.Status,
            FileName = (string?)null,
            FilePath = (string?)null,
            ErrorMessage = (string?)null,
            AttemptCount = 0,
            LastAttemptAt = (DateTime?)null,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = (DateTime?)null
        }, transaction);

        await _auditLogService.LogAsync(
            connection,
            transaction,
            workspaceId,
            "ExportRequested",
            EntityType.ExportJob,
            job.Id,
            null,
            JsonSerializer.Serialize(new { scope }),
            clientRequestId: clientRequestId);

        await _idempotencyService.CompleteAsync(
            workspaceId,
            userId,
            "export:create",
            clientRequestId,
            202,
            JsonSerializer.Serialize(job),
            EntityType.ExportJob,
            job.Id,
            connection,
            transaction,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return job;
    }

    public async Task<CloudExportJobDto?> GetJobAsync(Guid workspaceId, Guid exportId, CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT * FROM ""CloudExportJobs""
            WHERE ""WorkspaceId"" = @workspaceId AND ""Id"" = @exportId;";

        var row = await connection.QueryFirstOrDefaultAsync(sql, new { workspaceId, exportId });
        if (row == null) return null;

        return MapJob(row);
    }

    public async Task RecordDownloadAsync(Guid workspaceId, Guid exportId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await _auditLogService.LogAsync(
            workspaceId,
            "ExportDownloaded",
            EntityType.ExportJob,
            exportId,
            null,
            null,
            ipAddress: ipAddress,
            userAgent: userAgent);
    }

    public async Task ProcessPendingJobsAsync(CancellationToken cancellationToken = default)
    {
        await CleanupExpiredExportsAsync(cancellationToken);

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT * FROM ""CloudExportJobs""
            WHERE ""Status"" = @pending
               OR (""Status"" = @failed AND ""AttemptCount"" < @maxRetryCount)
            ORDER BY ""CreatedAt"" ASC
            LIMIT 10;";

        var rows = await connection.QueryAsync(sql, new
        {
            pending = EmergencyDraftStatus.Pending,
            failed = EmergencyDraftStatus.Failed,
            maxRetryCount = Math.Max(1, _options.ExportMaxRetryCount)
        });
        var jobs = rows.Select(MapJob).ToList();

        foreach (var job in jobs)
        {
            var attemptCount = await IncrementAttemptAsync(job.Id, cancellationToken);
            try
            {
                await ProcessJobAsync(job, cancellationToken);
            }
            catch (Exception ex)
            {
                await MarkJobFailedAsync(job, attemptCount, ex.Message, cancellationToken);
            }
        }
    }

    private async Task ProcessJobAsync(CloudExportJobDto job, CancellationToken cancellationToken)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var tempDirectory = EnsureWorkspaceExportDirectory(job.WorkspaceId);
        var tempFile = Path.Combine(tempDirectory, $"{job.Id:N}.tmp");
        try
        {
            using (var zipStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                await AddCommerceWorksheetAsync(connection, job.WorkspaceId, archive, "orders.xlsx", "orders", "CommerceOrders", OrderHeaders, MapOrderRow, cancellationToken);
                await AddCommerceWorksheetAsync(connection, job.WorkspaceId, archive, "customers.xlsx", "customers", "CommerceCustomers", CustomerHeaders, MapCustomerRow, cancellationToken);
                await AddCommerceWorksheetAsync(connection, job.WorkspaceId, archive, "products.xlsx", "products", "CommerceProducts", ProductHeaders, MapProductRow, cancellationToken);
                await AddCommerceWorksheetAsync(connection, job.WorkspaceId, archive, "inventory.xlsx", "inventory", "CommerceInventoryItems", InventoryHeaders, MapInventoryRow, cancellationToken);
                await AddCommerceWorksheetAsync(connection, job.WorkspaceId, archive, "cash-flow.xlsx", "cash-flow", "CommerceCashFlowEntries", CashFlowHeaders, MapCashFlowRow, cancellationToken);
                await AddWorksheetAsync(
                    connection,
                    archive,
                    "price-change-requests.xlsx",
                    "price-change",
                    @"SELECT * FROM ""CloudPriceChangeRequests"" WHERE ""WorkspaceId"" = @workspaceId ORDER BY ""RequestedAt"" DESC;",
                    new { workspaceId = job.WorkspaceId },
                    PriceChangeHeaders,
                    MapPriceChangeRow,
                    cancellationToken);
                await AddWorksheetAsync(
                    connection,
                    archive,
                    "audit-logs.xlsx",
                    "audit-logs",
                    @"SELECT * FROM ""CloudAuditLogs"" WHERE ""WorkspaceId"" = @workspaceId ORDER BY ""OccurredAt"" DESC;",
                    new { workspaceId = job.WorkspaceId },
                    AuditLogHeaders,
                    MapAuditLogRow,
                    cancellationToken);
                await AddWorksheetAsync(
                    connection,
                    archive,
                    "archive.xlsx",
                    "archive",
                    BuildArchiveSql(),
                    new { workspaceId = job.WorkspaceId },
                    ArchiveHeaders,
                    MapArchiveRow,
                    cancellationToken);
            }

            var fileName = $"business-package-{job.WorkspaceId:N}-{job.Id:N}.zip";
            string filePath;

            if (_blobStorage.IsEnabled)
            {
                var key = $"{_options.OssExportPrefix}{job.WorkspaceId:N}/{fileName}";
                await using var uploadStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read);
                await _blobStorage.UploadAsync(key, uploadStream, cancellationToken);
                filePath = key;
            }
            else
            {
                EnsureLocalExportCapacity(new FileInfo(tempFile).Length);
                var localPath = Path.Combine(tempDirectory, fileName);
                File.Move(tempFile, localPath, overwrite: true);
                filePath = localPath;
            }

            await UpdateJobStatusAsync(job.Id, EmergencyDraftStatus.Submitted, fileName, filePath, null, cancellationToken);
            await _auditLogService.LogAsync(
                job.WorkspaceId,
                "ExportCompleted",
                EntityType.ExportJob,
                job.Id,
                null,
                JsonSerializer.Serialize(new { fileName, storage = _blobStorage.IsEnabled ? "oss" : "local" }));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private async Task AddCommerceWorksheetAsync(
        System.Data.IDbConnection connection,
        Guid workspaceId,
        ZipArchive archive,
        string entryName,
        string sheetName,
        string tableName,
        IReadOnlyList<string> headers,
        Action<IXLWorksheet, int, dynamic> mapRow,
        CancellationToken cancellationToken)
    {
        var sql = $@"SELECT * FROM ""{tableName}"" WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0;";
        await AddWorksheetAsync(connection, archive, entryName, sheetName, sql, new { workspaceId }, headers, mapRow, cancellationToken);
    }

    private async Task AddWorksheetAsync(
        System.Data.IDbConnection connection,
        ZipArchive archive,
        string entryName,
        string sheetName,
        string sql,
        object parameters,
        IReadOnlyList<string> headers,
        Action<IXLWorksheet, int, dynamic> mapRow,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(sheetName);

        for (var i = 0; i < headers.Count; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }

        var rows = await connection.QueryAsync(sql, parameters);

        var rowIndex = 2;
        foreach (var row in rows)
        {
            mapRow(worksheet, rowIndex, row);
            rowIndex++;
        }

        if (headers.Count > 0)
        {
            worksheet.Range(1, 1, 1, headers.Count).Style.Font.Bold = true;
            worksheet.Columns(1, headers.Count).AdjustToContents();
        }

        workbook.SaveAs(entryStream);
    }

    private static void MapOrderRow(IXLWorksheet ws, int row, dynamic r)
    {
        ws.Cell(row, 1).Value = ((Guid)r.Id).ToString("N");
        ws.Cell(row, 2).Value = (string)r.OrderNo;
        ws.Cell(row, 3).Value = r.CustomerId is Guid cid ? cid.ToString() : string.Empty;
        ws.Cell(row, 4).Value = (int)r.SalesStage;
        ws.Cell(row, 5).Value = (int)r.PaymentStage;
        ws.Cell(row, 6).Value = (int)r.FulfillmentStage;
        ws.Cell(row, 7).Value = (decimal)r.Total;
        ws.Cell(row, 8).Value = (decimal)r.PaidAmount;
        ws.Cell(row, 9).Value = (decimal)r.ReceivableAmount;
        ws.Cell(row, 10).Value = (DateTime)r.OrderedAt;
        ws.Cell(row, 11).Value = (string?)r.Note ?? string.Empty;
    }

    private static void MapCustomerRow(IXLWorksheet ws, int row, dynamic r)
    {
        ws.Cell(row, 1).Value = ((Guid)r.Id).ToString("N");
        ws.Cell(row, 2).Value = (string)r.Name;
        ws.Cell(row, 3).Value = (string?)r.Phone ?? string.Empty;
        ws.Cell(row, 4).Value = (string?)r.WeChat ?? string.Empty;
        ws.Cell(row, 5).Value = (string?)r.Email ?? string.Empty;
        ws.Cell(row, 6).Value = (decimal)r.TotalSpend;
        ws.Cell(row, 7).Value = (int)r.CompletedOrderCount;
    }

    private static void MapProductRow(IXLWorksheet ws, int row, dynamic r)
    {
        ws.Cell(row, 1).Value = ((Guid)r.Id).ToString("N");
        ws.Cell(row, 2).Value = (string)r.Name;
        ws.Cell(row, 3).Value = (string)r.Code;
        ws.Cell(row, 4).Value = (int)r.ProductType;
        ws.Cell(row, 5).Value = (decimal)r.DefaultPrice;
        ws.Cell(row, 6).Value = (decimal?)r.DefaultCost ?? 0m;
    }

    private static void MapInventoryRow(IXLWorksheet ws, int row, dynamic r)
    {
        ws.Cell(row, 1).Value = ((Guid)r.Id).ToString("N");
        ws.Cell(row, 2).Value = (string)r.Name;
        ws.Cell(row, 3).Value = (string?)r.Sku ?? string.Empty;
        ws.Cell(row, 4).Value = (decimal)r.QuantityAvailable;
        ws.Cell(row, 5).Value = (decimal)r.ReorderThreshold;
        ws.Cell(row, 6).Value = (decimal?)r.UnitCost ?? 0m;
    }

    private static void MapCashFlowRow(IXLWorksheet ws, int row, dynamic r)
    {
        ws.Cell(row, 1).Value = ((Guid)r.Id).ToString("N");
        ws.Cell(row, 2).Value = (int)r.Direction;
        ws.Cell(row, 3).Value = (decimal)r.Amount;
        ws.Cell(row, 4).Value = (decimal)r.SettledAmount;
        ws.Cell(row, 5).Value = (int)r.SettlementStatus;
        ws.Cell(row, 6).Value = (DateTime)r.OccurredAt;
        ws.Cell(row, 7).Value = (string)r.CategoryName;
    }

    private static void MapPriceChangeRow(IXLWorksheet ws, int row, dynamic r)
    {
        ws.Cell(row, 1).Value = ((Guid)r.Id).ToString("N");
        ws.Cell(row, 2).Value = ((Guid)r.ProductId).ToString("N");
        ws.Cell(row, 3).Value = (decimal)r.CurrentPrice;
        ws.Cell(row, 4).Value = (decimal)r.ProposedPrice;
        ws.Cell(row, 5).Value = (string?)r.Reason ?? string.Empty;
        ws.Cell(row, 6).Value = (string)r.Status;
        ws.Cell(row, 7).Value = ((Guid)r.RequestedByUserId).ToString("N");
        ws.Cell(row, 8).Value = (DateTime)r.RequestedAt;
        ws.Cell(row, 9).Value = r.ReviewedByUserId is Guid reviewerId ? reviewerId.ToString("N") : string.Empty;
        ws.Cell(row, 10).Value = r.ReviewedAt is DateTime reviewedAt ? reviewedAt : string.Empty;
        ws.Cell(row, 11).Value = (string?)r.ReviewNote ?? string.Empty;
        ws.Cell(row, 12).Value = r.AppliedProductRevision is long revision ? revision : string.Empty;
    }

    private static void MapAuditLogRow(IXLWorksheet ws, int row, dynamic r)
    {
        ws.Cell(row, 1).Value = ((Guid)r.Id).ToString("N");
        ws.Cell(row, 2).Value = r.ActorUserId is Guid actorId ? actorId.ToString("N") : string.Empty;
        ws.Cell(row, 3).Value = (string)r.ActorDisplayName;
        ws.Cell(row, 4).Value = (string)r.ActorRole;
        ws.Cell(row, 5).Value = (string)r.Action;
        ws.Cell(row, 6).Value = (string)r.EntityType;
        ws.Cell(row, 7).Value = r.EntityId is Guid entityId ? entityId.ToString("N") : string.Empty;
        ws.Cell(row, 8).Value = (string?)r.Reason ?? string.Empty;
        ws.Cell(row, 9).Value = (string?)r.ClientRequestId ?? string.Empty;
        ws.Cell(row, 10).Value = (DateTime)r.OccurredAt;
        ws.Cell(row, 11).Value = (string?)r.IpAddress ?? string.Empty;
        ws.Cell(row, 12).Value = (string?)r.UserAgent ?? string.Empty;
    }

    private static void MapArchiveRow(IXLWorksheet ws, int row, dynamic r)
    {
        ws.Cell(row, 1).Value = (string)r.EntityType;
        ws.Cell(row, 2).Value = ((Guid)r.Id).ToString("N");
        ws.Cell(row, 3).Value = (DateTime)r.UpdatedAt;
        ws.Cell(row, 4).Value = r.ArchivedByUserId is Guid archivedBy ? archivedBy.ToString("N") : string.Empty;
        ws.Cell(row, 5).Value = (string?)r.ArchiveReason ?? string.Empty;
    }

    private static string BuildArchiveSql() => @"
        SELECT 'product' AS ""EntityType"", ""Id"", ""UpdatedAt"", ""ArchivedByUserId"", ""ArchiveReason"" FROM ""CommerceProducts"" WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 1
        UNION ALL
        SELECT 'inventoryItem' AS ""EntityType"", ""Id"", ""UpdatedAt"", ""ArchivedByUserId"", ""ArchiveReason"" FROM ""CommerceInventoryItems"" WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 1
        UNION ALL
        SELECT 'customer' AS ""EntityType"", ""Id"", ""UpdatedAt"", ""ArchivedByUserId"", ""ArchiveReason"" FROM ""CommerceCustomers"" WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 1
        UNION ALL
        SELECT 'order' AS ""EntityType"", ""Id"", ""UpdatedAt"", ""ArchivedByUserId"", ""ArchiveReason"" FROM ""CommerceOrders"" WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 1
        UNION ALL
        SELECT 'cashFlowEntry' AS ""EntityType"", ""Id"", ""UpdatedAt"", ""ArchivedByUserId"", ""ArchiveReason"" FROM ""CommerceCashFlowEntries"" WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 1
        UNION ALL
        SELECT 'businessTask' AS ""EntityType"", ""Id"", ""UpdatedAt"", ""ArchivedByUserId"", ""ArchiveReason"" FROM ""CommerceBusinessTasks"" WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 1
        UNION ALL
        SELECT 'businessInsight' AS ""EntityType"", ""Id"", ""UpdatedAt"", ""ArchivedByUserId"", ""ArchiveReason"" FROM ""CommerceBusinessInsights"" WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 1
        ORDER BY ""UpdatedAt"" DESC;";

    private async Task UpdateJobStatusAsync(Guid id, string status, string? fileName, string? filePath, string? error, CancellationToken cancellationToken)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            UPDATE ""CloudExportJobs""
            SET ""Status"" = @status,
                ""FileName"" = @fileName,
                ""FilePath"" = @filePath,
                ""ErrorMessage"" = @error,
                ""CompletedAt"" = @completedAt
            WHERE ""Id"" = @id;";

        await connection.ExecuteAsync(sql, new
        {
            id,
            status,
            fileName,
            filePath,
            error,
            completedAt = DateTime.UtcNow
        });
    }

    private async Task<int> IncrementAttemptAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(
            @"UPDATE ""CloudExportJobs""
              SET ""AttemptCount"" = ""AttemptCount"" + 1,
                  ""LastAttemptAt"" = @now
              WHERE ""Id"" = @id
              RETURNING ""AttemptCount"";",
            new { id, now = DateTime.UtcNow });
    }

    private async Task MarkJobFailedAsync(CloudExportJobDto job, int attemptCount, string error, CancellationToken cancellationToken)
    {
        await UpdateJobStatusAsync(job.Id, EmergencyDraftStatus.Failed, null, null, error, cancellationToken);
        await _auditLogService.LogAsync(
            job.WorkspaceId,
            "ExportFailed",
            EntityType.ExportJob,
            job.Id,
            null,
            JsonSerializer.Serialize(new
            {
                attemptCount,
                maxRetryCount = Math.Max(1, _options.ExportMaxRetryCount),
                error
            }));
    }

    private async Task CleanupExpiredExportsAsync(CancellationToken cancellationToken)
    {
        var retentionHours = Math.Max(1, _options.ExportRetentionHours);
        var cutoff = DateTime.UtcNow.AddHours(-retentionHours);

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync(
            @"SELECT * FROM ""CloudExportJobs""
              WHERE ""Status"" = @submitted
                AND ""CompletedAt"" IS NOT NULL
                AND ""CompletedAt"" < @cutoff;",
            new { submitted = EmergencyDraftStatus.Submitted, cutoff });

        foreach (var row in rows)
        {
            var job = MapJob(row);
            await DeleteExportFileAsync(job.FilePath, cancellationToken);
            await connection.ExecuteAsync(
                @"UPDATE ""CloudExportJobs""
                  SET ""Status"" = @expired,
                      ""FileName"" = NULL,
                      ""FilePath"" = NULL,
                      ""ErrorMessage"" = @error
                  WHERE ""Id"" = @id;",
                new { id = job.Id, expired = "Expired", error = $"导出文件已超过 {retentionHours} 小时保留期并清理。" });
        }

        CleanupLocalOrphans(cutoff);
    }

    private async Task DeleteExportFileAsync(string? filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (_blobStorage.IsEnabled && !Path.IsPathRooted(filePath))
        {
            await _blobStorage.DeleteAsync(filePath, cancellationToken);
            return;
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!IsUnderDirectory(fullPath, Path.GetFullPath(_options.LocalExportDirectory)))
        {
            return;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private void CleanupLocalOrphans(DateTime cutoff)
    {
        var root = Path.GetFullPath(_options.LocalExportDirectory);
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var file = new FileInfo(path);
            if (file.LastWriteTimeUtc < cutoff && IsUnderDirectory(file.FullName, root))
            {
                file.Delete();
            }
        }
    }

    private string EnsureWorkspaceExportDirectory(Guid workspaceId)
    {
        var root = Path.GetFullPath(_options.LocalExportDirectory);
        var workspaceDir = Path.Combine(root, workspaceId.ToString("N"));
        Directory.CreateDirectory(workspaceDir);
        return workspaceDir;
    }

    private void EnsureLocalExportCapacity(long extraBytes)
    {
        var maxBytes = _options.ExportMaxLocalBytes;
        if (maxBytes <= 0)
        {
            return;
        }

        var root = Path.GetFullPath(_options.LocalExportDirectory);
        Directory.CreateDirectory(root);
        var currentBytes = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .Sum(file => file.Length);

        if (currentBytes + extraBytes > maxBytes)
        {
            throw new InvalidOperationException($"导出目录已超过容量上限，请清理 {root} 后重试。");
        }
    }

    private static bool IsUnderDirectory(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeRequestHash<T>(T request)
    {
        var json = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static CloudExportJobDto MapJob(dynamic row) => new()
    {
        Id = (Guid)row.Id,
        WorkspaceId = (Guid)row.WorkspaceId,
        RequestedByUserId = (Guid)row.RequestedByUserId,
        Scope = (string)row.Scope,
        Status = (string)row.Status,
        FileName = row.FileName,
        FilePath = row.FilePath,
        DownloadUrl = row.FileName is null ? null : $"api/workspaces/{(Guid)row.WorkspaceId:N}/exports/{(Guid)row.Id:N}/download",
        ErrorMessage = row.ErrorMessage,
        AttemptCount = (int)row.AttemptCount,
        LastAttemptAtUtc = (DateTime?)row.LastAttemptAt,
        CompletedAtUtc = (DateTime?)row.CompletedAt
    };

    private static readonly string[] OrderHeaders =
        ["Id", "OrderNo", "CustomerId", "SalesStage", "PaymentStage", "FulfillmentStage", "Total", "PaidAmount", "ReceivableAmount", "OrderedAt", "Note"];

    private static readonly string[] CustomerHeaders =
        ["Id", "Name", "Phone", "WeChat", "Email", "TotalSpend", "CompletedOrderCount"];

    private static readonly string[] ProductHeaders =
        ["Id", "Name", "Code", "ProductType", "DefaultPrice", "DefaultCost"];

    private static readonly string[] InventoryHeaders =
        ["Id", "Name", "Sku", "QuantityAvailable", "ReorderThreshold", "UnitCost"];

    private static readonly string[] CashFlowHeaders =
        ["Id", "Direction", "Amount", "SettledAmount", "SettlementStatus", "OccurredAt", "CategoryName"];

    private static readonly string[] PriceChangeHeaders =
        ["Id", "ProductId", "CurrentPrice", "ProposedPrice", "Reason", "Status", "RequestedByUserId", "RequestedAt", "ReviewedByUserId", "ReviewedAt", "ReviewNote", "AppliedProductRevision"];

    private static readonly string[] AuditLogHeaders =
        ["Id", "ActorUserId", "ActorDisplayName", "ActorRole", "Action", "EntityType", "EntityId", "Reason", "ClientRequestId", "OccurredAt", "IpAddress", "UserAgent", "DeviceId", "Result", "CorrelationId"];

    private static readonly string[] ArchiveHeaders =
        ["EntityType", "Id", "UpdatedAt", "ArchivedByUserId", "ArchiveReason"];
}
