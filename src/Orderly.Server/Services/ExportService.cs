using System.IO.Compression;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Server.Data;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface IExportService
{
    Task<CloudExportJobDto> CreateJobAsync(Guid workspaceId, Guid userId, string scope, CancellationToken cancellationToken = default);
    Task<CloudExportJobDto?> GetJobAsync(Guid workspaceId, Guid exportId, CancellationToken cancellationToken = default);
    Task ProcessPendingJobsAsync(CancellationToken cancellationToken = default);
}

public sealed class ExportService : IExportService
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly IBlobStorage _blobStorage;
    private readonly ServerOptions _options;

    public ExportService(PostgresConnectionFactory connectionFactory, IBlobStorage blobStorage, ServerOptions options)
    {
        _connectionFactory = connectionFactory;
        _blobStorage = blobStorage;
        _options = options;
    }

    public async Task<CloudExportJobDto> CreateJobAsync(Guid workspaceId, Guid userId, string scope, CancellationToken cancellationToken = default)
    {
        var job = new CloudExportJobDto
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            RequestedByUserId = userId,
            Scope = scope,
            Status = EmergencyDraftStatus.Pending
        };

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO ""CloudExportJobs"" (
                ""Id"", ""WorkspaceId"", ""RequestedByUserId"", ""Scope"", ""Status"",
                ""FileName"", ""FilePath"", ""ErrorMessage"", ""CreatedAt"", ""CompletedAt"")
            VALUES (
                @Id, @WorkspaceId, @RequestedByUserId, @Scope, @Status,
                @FileName, @FilePath, @ErrorMessage, @CreatedAt, @CompletedAt);";

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
            CreatedAt = DateTime.UtcNow,
            CompletedAt = (DateTime?)null
        });

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

    public async Task ProcessPendingJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT * FROM ""CloudExportJobs""
            WHERE ""Status"" = @pending
            ORDER BY ""CreatedAt"" ASC
            LIMIT 10;";

        var rows = await connection.QueryAsync(sql, new { pending = EmergencyDraftStatus.Pending });
        var jobs = rows.Select(MapJob).ToList();

        foreach (var job in jobs)
        {
            try
            {
                await ProcessJobAsync(job, cancellationToken);
            }
            catch (Exception ex)
            {
                await UpdateJobStatusAsync(job.Id, EmergencyDraftStatus.Failed, null, ex.Message, cancellationToken);
            }
        }
    }

    private async Task ProcessJobAsync(CloudExportJobDto job, CancellationToken cancellationToken)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var tempFile = Path.GetTempFileName() + ".zip";
        try
        {
            using (var zipStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                await AddWorksheetAsync(connection, job.WorkspaceId, archive, "Orders", "CommerceOrders", MapOrderRow, cancellationToken);
                await AddWorksheetAsync(connection, job.WorkspaceId, archive, "Customers", "CommerceCustomers", MapCustomerRow, cancellationToken);
                await AddWorksheetAsync(connection, job.WorkspaceId, archive, "Products", "CommerceProducts", MapProductRow, cancellationToken);
                await AddWorksheetAsync(connection, job.WorkspaceId, archive, "Inventory", "CommerceInventoryItems", MapInventoryRow, cancellationToken);
                await AddWorksheetAsync(connection, job.WorkspaceId, archive, "CashFlow", "CommerceCashFlowEntries", MapCashFlowRow, cancellationToken);
            }

            var fileName = $"business-package-{job.WorkspaceId:N}-{job.Id:N}.zip";
            string? downloadUrl = null;

            if (_blobStorage.IsEnabled)
            {
                var key = $"{_options.OssExportPrefix}{job.WorkspaceId:N}/{fileName}";
                await using var uploadStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read);
                await _blobStorage.UploadAsync(key, uploadStream, cancellationToken);
                downloadUrl = $"api/workspaces/{job.WorkspaceId:N}/exports/{job.Id:N}/download";
            }
            else
            {
                // If OSS is not configured, store locally and return a local API URL.
                var localDir = Path.Combine(Path.GetTempPath(), "orderly-exports", job.WorkspaceId.ToString("N"));
                Directory.CreateDirectory(localDir);
                var localPath = Path.Combine(localDir, fileName);
                File.Move(tempFile, localPath, overwrite: true);
                downloadUrl = $"api/workspaces/{job.WorkspaceId:N}/exports/{job.Id:N}/download";
            }

            await UpdateJobStatusAsync(job.Id, EmergencyDraftStatus.Submitted, downloadUrl, null, cancellationToken);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private async Task AddWorksheetAsync(
        System.Data.IDbConnection connection,
        Guid workspaceId,
        ZipArchive archive,
        string sheetName,
        string tableName,
        Action<IXLWorksheet, int, dynamic> mapRow,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry($"{sheetName}.xlsx", CompressionLevel.Optimal);
        await using var entryStream = entry.Open();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(sheetName);

        var sql = $@"SELECT * FROM ""{tableName}"" WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0;";
        var rows = await connection.QueryAsync(sql, new { workspaceId });

        var rowIndex = 1;
        foreach (var row in rows)
        {
            mapRow(worksheet, rowIndex + 1, row);
            rowIndex++;
        }

        if (rowIndex > 1)
        {
            // Add a header row based on the first data row cells.
            var firstRow = worksheet.Row(2);
            var colIndex = 1;
            foreach (var cell in firstRow.CellsUsed())
            {
                worksheet.Cell(1, colIndex).Value = cell.Address.ColumnLetter;
                colIndex++;
            }
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

    private async Task UpdateJobStatusAsync(Guid id, string status, string? downloadUrl, string? error, CancellationToken cancellationToken)
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
            fileName = downloadUrl,
            filePath = downloadUrl,
            error,
            completedAt = DateTime.UtcNow
        });
    }

    private static CloudExportJobDto MapJob(dynamic row) => new()
    {
        Id = (Guid)row.Id,
        WorkspaceId = (Guid)row.WorkspaceId,
        RequestedByUserId = (Guid)row.RequestedByUserId,
        Scope = (string)row.Scope,
        Status = (string)row.Status,
        FileName = row.FileName,
        DownloadUrl = row.FileName,
        ErrorMessage = row.ErrorMessage,
        CompletedAtUtc = (DateTime?)row.CompletedAt
    };
}
