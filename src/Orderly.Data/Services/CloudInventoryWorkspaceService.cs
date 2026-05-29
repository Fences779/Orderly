using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class CloudInventoryWorkspaceService : IInventoryWorkspaceService
{
    private static readonly string[] ExportHeaders =
    [
        "物料编码",
        "材料名称",
        "分类",
        "单位",
        "当前库存",
        "安全库存",
        "7日动销",
        "30日动销",
        "单位成本",
        "平均每串售价",
        "平均每串成本",
        "供应商",
        "备注",
        "启用",
        "最近补货时间",
        "源数据更新时间"
    ];

    private static readonly string[] MaterialCodeAliases = ["物料编码", "材料编码", "sku", "skuid", "sku编码", "materialcode", "materialid", "material_id", "materialcodeid"];
    private static readonly string[] MaterialNameAliases = ["材料名称", "物料名称", "名称", "materialname", "name", "title"];
    private static readonly string[] CategoryAliases = ["分类", "类目", "category"];
    private static readonly string[] StockUnitAliases = ["单位", "库存单位", "stockunit", "unit"];
    private static readonly string[] CurrentStockAliases = ["当前库存", "库存", "现有库存", "currentstock", "currentstockqty", "stockqty"];
    private static readonly string[] SafeStockAliases = ["安全库存", "安全库存建议", "safestock", "safestocksuggestedqty", "safestockqty"];
    private static readonly string[] Sold7dAliases = ["7日动销", "7日消耗", "7天动销", "sold7d", "sold7dqty"];
    private static readonly string[] Sold30dAliases = ["30日动销", "30日消耗", "30天动销", "sold30d", "sold30dqty"];
    private static readonly string[] UnitCostAliases = ["单位成本", "成本", "unitcost", "costprice", "purchaseprice"];
    private static readonly string[] BraceletSalePriceAliases = ["平均每串售价", "串售价", "braceletsaleprice", "saleprice", "baseprice"];
    private static readonly string[] BraceletCostPriceAliases = ["平均每串成本", "串成本", "braceletcostprice", "braceletcost", "costperbracelet"];
    private static readonly string[] SupplierAliases = ["供应商", "supplier", "suppliername"];
    private static readonly string[] RemarkAliases = ["备注", "remark", "inventoryremark", "note"];
    private static readonly string[] EnabledAliases = ["启用", "状态", "enabled", "isenabled"];
    private static readonly string[] LastRestockedAliases = ["最近补货时间", "最近补货", "lastrestockedat", "restockedat"];
    private static readonly string[] SourceUpdatedAliases = ["源数据更新时间", "更新时间", "sourceupdatedat", "updatedat"];

    private readonly InventoryGatewayClient _client;

    public CloudInventoryWorkspaceService(InventoryGatewayClient client)
    {
        _client = client;
    }

    public async Task<StringNarrationInventoryManagementDashboardResult> GetDashboardAsync(
        StringNarrationInventoryManagementDashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new StringNarrationInventoryManagementDashboardRequest();
        var root = await _client.InvokeAsync("inventoryDashboard", request, cancellationToken);
        return ParseDashboard(root);
    }

    public async Task<InventoryImportPreviewResult> PrepareWorkbookImportAsync(
        string workbookPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
        {
            throw new InvalidOperationException("请选择要导入的 Excel 文件。");
        }

        var rows = LoadWorkbookRows(workbookPath, out var rowErrors);
        if (rows.Count == 0 && rowErrors.Count == 0)
        {
            rowErrors.Add(new InventoryImportRowError { RowNumber = 0, Message = "Excel 中没有可导入的数据行。" });
        }

        var remoteRows = await FetchExportRowsAsync(cancellationToken);
        var remoteMap = remoteRows.ToDictionary(row => row.MaterialCode, StringComparer.OrdinalIgnoreCase);

        var insertedCount = 0;
        var updatedCount = 0;
        var unchangedCount = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!remoteMap.TryGetValue(row.MaterialCode, out var current))
            {
                insertedCount++;
                continue;
            }

            if (RowsEqual(current, row))
            {
                unchangedCount++;
            }
            else
            {
                updatedCount++;
            }
        }

        return new InventoryImportPreviewResult
        {
            SourcePath = workbookPath,
            SourceFileHash = ComputeFileHash(workbookPath),
            Rows = rows,
            Errors = rowErrors,
            InsertedCount = insertedCount,
            UpdatedCount = updatedCount,
            UnchangedCount = unchangedCount,
            SkippedCount = rowErrors.Count,
            LowStockCount = rows.Count(static row => row.IsLowStock),
            DisabledCount = rows.Count(static row => !row.Enabled)
        };
    }

    public async Task<InventoryImportCommitResult> CommitWorkbookImportAsync(
        InventoryImportPreviewResult preview,
        bool writeBackWorkbook,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (!preview.CanCommit)
        {
            throw new InvalidOperationException("当前导入预览存在错误，无法提交到云端。");
        }

        var root = await _client.InvokeAsync(
            "inventoryBulkUpsert",
            new
            {
                sourceFileName = preview.SourceFileName,
                sourceFileHash = preview.SourceFileHash,
                rows = preview.Rows.Select(MapRowPayload).ToList()
            },
            cancellationToken);

        string backupPath = string.Empty;
        if (writeBackWorkbook)
        {
            backupPath = CreateWorkbookBackup(preview.SourcePath);
            await ExportWorkbookAsync(preview.SourcePath, cancellationToken);
        }

        return new InventoryImportCommitResult
        {
            BatchNo = ReadString(root, "batchNo"),
            TotalRows = ReadInt(root, "totalRows"),
            InsertedCount = ReadInt(root, "insertedCount"),
            UpdatedCount = ReadInt(root, "updatedCount"),
            UnchangedCount = ReadInt(root, "unchangedCount"),
            SkippedCount = ReadInt(root, "skippedCount"),
            UpdatedAt = ReadLong(root, "updatedAt"),
            WorkbookPath = preview.SourcePath,
            BackupPath = backupPath
        };
    }

    public async Task ExportWorkbookAsync(
        string workbookPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
        {
            throw new InvalidOperationException("请选择导出的 Excel 文件路径。");
        }

        var rows = await FetchExportRowsAsync(cancellationToken);
        WriteWorkbook(workbookPath, rows);
    }

    private async Task<IReadOnlyList<InventoryWorkbookRow>> FetchExportRowsAsync(CancellationToken cancellationToken)
    {
        var root = await _client.InvokeAsync("inventoryExportRows", new { }, cancellationToken);
        if (!root.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<InventoryWorkbookRow>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            rows.Add(new InventoryWorkbookRow
            {
                MaterialCode = ReadString(item, "materialCode"),
                MaterialName = ReadString(item, "materialName"),
                Category = ReadString(item, "category"),
                StockUnit = string.IsNullOrWhiteSpace(ReadString(item, "stockUnit")) ? "件" : ReadString(item, "stockUnit"),
                CurrentStockQty = ReadDecimal(item, "currentStockQty"),
                SafeStockQty = ReadDecimal(item, "safeStockQty"),
                Sold7dQty = ReadDecimal(item, "sold7dQty"),
                Sold30dQty = ReadDecimal(item, "sold30dQty"),
                UnitCost = ReadDecimal(item, "unitCost"),
                BraceletSalePrice = ReadDecimal(item, "braceletSalePrice"),
                BraceletCostPrice = ReadDecimal(item, "braceletCostPrice"),
                SupplierName = ReadString(item, "supplierName"),
                Remark = ReadString(item, "remark"),
                Enabled = ReadBool(item, "enabled", true),
                LastRestockedAt = ReadNullableDateTime(item, "lastRestockedAt"),
                SourceUpdatedAt = ReadNullableDateTime(item, "sourceUpdatedAt")
            });
        }

        return rows;
    }

    private static object MapRowPayload(InventoryWorkbookRow row)
    {
        return new
        {
            materialCode = row.MaterialCode,
            materialName = row.MaterialName,
            category = row.Category,
            stockUnit = row.StockUnit,
            currentStockQty = row.CurrentStockQty,
            safeStockQty = row.SafeStockQty,
            sold7dQty = row.Sold7dQty,
            sold30dQty = row.Sold30dQty,
            unitCost = row.UnitCost,
            braceletSalePrice = row.BraceletSalePrice,
            braceletCostPrice = row.BraceletCostPrice,
            supplierName = row.SupplierName,
            remark = row.Remark,
            enabled = row.Enabled,
            lastRestockedAt = row.LastRestockedAt?.ToString("O"),
            sourceUpdatedAt = row.SourceUpdatedAt?.ToString("O")
        };
    }

    private static List<InventoryWorkbookRow> LoadWorkbookRows(string workbookPath, out List<InventoryImportRowError> errors)
    {
        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException("未找到要导入的 Excel 文件。", workbookPath);
        }

        using var workbook = new XLWorkbook(workbookPath);
        var worksheet = workbook.Worksheets.FirstOrDefault(static sheet => string.Equals(sheet.Name, "Inventory", StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Excel 中没有可读取的工作表。");

        var headerRow = worksheet.FirstRowUsed() ?? throw new InvalidOperationException("Excel 缺少表头。");
        var headerMap = BuildHeaderMap(headerRow);

        EnsureRequiredHeader(headerMap, MaterialCodeAliases, "物料编码");
        EnsureRequiredHeader(headerMap, MaterialNameAliases, "材料名称");

        var rows = new List<InventoryWorkbookRow>();
        errors = new List<InventoryImportRowError>();

        var lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRowNumber; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty())
            {
                continue;
            }

            var materialCode = ReadCellText(row, headerMap, MaterialCodeAliases);
            var materialName = ReadCellText(row, headerMap, MaterialNameAliases);
            if (string.IsNullOrWhiteSpace(materialCode) && string.IsNullOrWhiteSpace(materialName))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(materialCode))
            {
                errors.Add(new InventoryImportRowError
                {
                    RowNumber = rowNumber,
                    Message = "缺少物料编码。"
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(materialName))
            {
                errors.Add(new InventoryImportRowError
                {
                    RowNumber = rowNumber,
                    Message = "缺少材料名称。"
                });
                continue;
            }

            try
            {
                rows.Add(new InventoryWorkbookRow
                {
                    RowNumber = rowNumber,
                    MaterialCode = materialCode,
                    MaterialName = materialName,
                    Category = ReadCellText(row, headerMap, CategoryAliases),
                    StockUnit = string.IsNullOrWhiteSpace(ReadCellText(row, headerMap, StockUnitAliases)) ? "件" : ReadCellText(row, headerMap, StockUnitAliases),
                    CurrentStockQty = ReadCellDecimal(row, headerMap, CurrentStockAliases),
                    SafeStockQty = ReadCellDecimal(row, headerMap, SafeStockAliases),
                    Sold7dQty = ReadCellDecimal(row, headerMap, Sold7dAliases),
                    Sold30dQty = ReadCellDecimal(row, headerMap, Sold30dAliases),
                    UnitCost = ReadCellDecimal(row, headerMap, UnitCostAliases),
                    BraceletSalePrice = ReadCellDecimal(row, headerMap, BraceletSalePriceAliases),
                    BraceletCostPrice = ReadCellDecimal(row, headerMap, BraceletCostPriceAliases),
                    SupplierName = ReadCellText(row, headerMap, SupplierAliases),
                    Remark = ReadCellText(row, headerMap, RemarkAliases),
                    Enabled = ReadCellBool(row, headerMap, EnabledAliases, true),
                    LastRestockedAt = ReadCellNullableDateTime(row, headerMap, LastRestockedAliases),
                    SourceUpdatedAt = ReadCellNullableDateTime(row, headerMap, SourceUpdatedAliases)
                });
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(new InventoryImportRowError
                {
                    RowNumber = rowNumber,
                    Message = ex.Message
                });
            }
        }

        return rows;
    }

    private static void WriteWorkbook(string workbookPath, IReadOnlyList<InventoryWorkbookRow> rows)
    {
        var directory = Path.GetDirectoryName(workbookPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Inventory");
        for (var column = 0; column < ExportHeaders.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = ExportHeaders[column];
            worksheet.Cell(1, column + 1).Style.Font.Bold = true;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            worksheet.Cell(excelRow, 1).Value = row.MaterialCode;
            worksheet.Cell(excelRow, 2).Value = row.MaterialName;
            worksheet.Cell(excelRow, 3).Value = row.Category;
            worksheet.Cell(excelRow, 4).Value = row.StockUnit;
            worksheet.Cell(excelRow, 5).Value = row.CurrentStockQty;
            worksheet.Cell(excelRow, 6).Value = row.SafeStockQty;
            worksheet.Cell(excelRow, 7).Value = row.Sold7dQty;
            worksheet.Cell(excelRow, 8).Value = row.Sold30dQty;
            worksheet.Cell(excelRow, 9).Value = row.UnitCost;
            worksheet.Cell(excelRow, 10).Value = row.BraceletSalePrice;
            worksheet.Cell(excelRow, 11).Value = row.BraceletCostPrice;
            worksheet.Cell(excelRow, 12).Value = row.SupplierName;
            worksheet.Cell(excelRow, 13).Value = row.Remark;
            worksheet.Cell(excelRow, 14).Value = row.Enabled ? "是" : "否";
            worksheet.Cell(excelRow, 15).Value = row.LastRestockedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
            worksheet.Cell(excelRow, 16).Value = row.SourceUpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(workbookPath);
    }

    private static string CreateWorkbookBackup(string workbookPath)
    {
        if (!File.Exists(workbookPath))
        {
            return string.Empty;
        }

        var directory = Path.GetDirectoryName(workbookPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(workbookPath);
        var extension = Path.GetExtension(workbookPath);
        var backupPath = Path.Combine(directory, $"{fileName}.{DateTime.Now:yyyyMMdd-HHmmss}.bak{extension}");
        File.Copy(workbookPath, backupPath, overwrite: true);
        return backupPath;
    }

    private static StringNarrationInventoryManagementDashboardResult ParseDashboard(JsonElement root)
    {
        var items = new List<StringNarrationInventoryManagementDashboardItem>();
        if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                items.Add(new StringNarrationInventoryManagementDashboardItem
                {
                    MaterialId = ReadString(item, "materialCode"),
                    MaterialName = ReadString(item, "materialName"),
                    Category = ReadString(item, "category"),
                    CurrentStockQty = ReadDecimal(item, "currentStockQty"),
                    StockUnit = ReadString(item, "stockUnit"),
                    Sold7dQty = ReadDecimal(item, "sold7dQty"),
                    Sold7dRatio = ReadDecimal(item, "sold7dRatio"),
                    Sold30dQty = ReadDecimal(item, "sold30dQty"),
                    Sold30dRatio = ReadDecimal(item, "sold30dRatio"),
                    Consumed7dQty = ReadDecimal(item, "sold7dQty"),
                    Consumed30dQty = ReadDecimal(item, "sold30dQty"),
                    SafeStockSuggestedQty = ReadNullableDecimal(item, "safeStockQty"),
                    Status = ReadString(item, "status"),
                    StatusLabel = ReadString(item, "statusLabel"),
                    UnitCost = ReadNullableDecimal(item, "unitCost"),
                    LastRestockedAt = ToUnixMilliseconds(ReadNullableDateTime(item, "lastRestockedAt")),
                    SupplierName = ReadString(item, "supplierName"),
                    Remark = ReadString(item, "remark")
                });
            }
        }

        var summaryElement = root.TryGetProperty("summary", out var tempSummary) && tempSummary.ValueKind == JsonValueKind.Object
            ? tempSummary
            : default;
        var filterOptionsElement = root.TryGetProperty("filterOptions", out var tempFilterOptions) && tempFilterOptions.ValueKind == JsonValueKind.Object
            ? tempFilterOptions
            : default;
        var pageInfoElement = root.TryGetProperty("pageInfo", out var tempPageInfo) && tempPageInfo.ValueKind == JsonValueKind.Object
            ? tempPageInfo
            : default;

        return new StringNarrationInventoryManagementDashboardResult
        {
            UpdatedAt = ReadLong(root, "updatedAt"),
            Summary = new StringNarrationInventoryManagementDashboardSummary
            {
                AvgOrderMaterialUsage = ReadNullableDecimal(summaryElement, "avgOrderMaterialUsage"),
                AvgMaterialUnitCost = ReadNullableDecimal(summaryElement, "avgMaterialUnitCost"),
                AvgBraceletSalePrice = ReadNullableDecimal(summaryElement, "avgBraceletSalePrice"),
                AvgBraceletCostPrice = ReadNullableDecimal(summaryElement, "avgBraceletCostPrice"),
                GrossMarginRate = ReadNullableDecimal(summaryElement, "grossMarginRate"),
                LowStockCount = ReadInt(summaryElement, "lowStockCount"),
                FastSellingCount = ReadInt(summaryElement, "fastSellingCount"),
                LowSellingCount = ReadInt(summaryElement, "lowSellingCount"),
                InventoryHealthStatus = ReadString(summaryElement, "inventoryHealthStatus"),
                InventoryHealthSummary = ReadString(summaryElement, "inventoryHealthSummary"),
                InventoryWarningCount = ReadInt(summaryElement, "inventoryWarningCount")
            },
            FilterOptions = new StringNarrationInventoryManagementDashboardFilterOptions
            {
                Categories = ReadStringArray(filterOptionsElement, "categories"),
                Statuses = ReadStatusOptions(filterOptionsElement),
                DefaultSortBy = ReadString(filterOptionsElement, "defaultSortBy"),
                DefaultSortDirection = ReadString(filterOptionsElement, "defaultSortDirection")
            },
            Items = items,
            PageInfo = new StringNarrationInventoryManagementDashboardPageInfo
            {
                Page = Math.Max(1, ReadInt(pageInfoElement, "page")),
                PageSize = Math.Max(1, ReadInt(pageInfoElement, "pageSize")),
                Total = ReadInt(pageInfoElement, "total"),
                TotalPages = Math.Max(1, ReadInt(pageInfoElement, "totalPages"))
            }
        };
    }

    private static IReadOnlyList<StringNarrationInventoryManagementDashboardFilterOption> ReadStatusOptions(JsonElement filterOptionsElement)
    {
        if (!(filterOptionsElement.ValueKind == JsonValueKind.Object
              && filterOptionsElement.TryGetProperty("statuses", out var statusesElement)
              && statusesElement.ValueKind == JsonValueKind.Array))
        {
            return
            [
                new StringNarrationInventoryManagementDashboardFilterOption { Value = "all", Label = "全部状态" },
                new StringNarrationInventoryManagementDashboardFilterOption { Value = "fast_selling", Label = "动销偏快" },
                new StringNarrationInventoryManagementDashboardFilterOption { Value = "low_selling", Label = "低动销" },
                new StringNarrationInventoryManagementDashboardFilterOption { Value = "normal", Label = "正常" },
                new StringNarrationInventoryManagementDashboardFilterOption { Value = "low_stock", Label = "低库存" },
                new StringNarrationInventoryManagementDashboardFilterOption { Value = "disabled", Label = "停用" }
            ];
        }

        var options = new List<StringNarrationInventoryManagementDashboardFilterOption>();
        foreach (var item in statusesElement.EnumerateArray())
        {
            options.Add(new StringNarrationInventoryManagementDashboardFilterOption
            {
                Value = ReadString(item, "value"),
                Label = ReadString(item, "label")
            });
        }

        return options;
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var normalized = NormalizeHeader(cell.GetString());
            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
            {
                map[normalized] = cell.Address.ColumnNumber;
            }
        }

        return map;
    }

    private static void EnsureRequiredHeader(Dictionary<string, int> headerMap, IEnumerable<string> aliases, string displayName)
    {
        if (ResolveColumnIndex(headerMap, aliases) > 0)
        {
            return;
        }

        throw new InvalidOperationException($"Excel 缺少必填列：{displayName}。");
    }

    private static int ResolveColumnIndex(Dictionary<string, int> headerMap, IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (headerMap.TryGetValue(NormalizeHeader(alias), out var columnIndex))
            {
                return columnIndex;
            }
        }

        return 0;
    }

    private static string ReadCellText(IXLRow row, Dictionary<string, int> headerMap, IEnumerable<string> aliases)
    {
        var columnIndex = ResolveColumnIndex(headerMap, aliases);
        if (columnIndex <= 0)
        {
            return string.Empty;
        }

        return row.Cell(columnIndex).GetFormattedString().Trim();
    }

    private static decimal ReadCellDecimal(IXLRow row, Dictionary<string, int> headerMap, IEnumerable<string> aliases)
    {
        var text = ReadCellText(row, headerMap, aliases);
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var current))
        {
            return current;
        }

        throw new InvalidOperationException($"无法解析数字：{text}");
    }

    private static bool ReadCellBool(IXLRow row, Dictionary<string, int> headerMap, IEnumerable<string> aliases, bool defaultValue)
    {
        var text = ReadCellText(row, headerMap, aliases);
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        var normalized = text.Trim().ToLowerInvariant();
        if (normalized is "1" or "true" or "yes" or "y" or "是" or "启用" or "正常")
        {
            return true;
        }

        if (normalized is "0" or "false" or "no" or "n" or "否" or "停用" or "禁用" or "disabled")
        {
            return false;
        }

        return defaultValue;
    }

    private static DateTime? ReadCellNullableDateTime(IXLRow row, Dictionary<string, int> headerMap, IEnumerable<string> aliases)
    {
        var text = ReadCellText(row, headerMap, aliases);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"无法解析日期时间：{text}");
    }

    private static string NormalizeHeader(string value)
    {
        return string.Concat((value ?? string.Empty)
            .Trim()
            .Where(static ch => !char.IsWhiteSpace(ch) && ch is not '_' and not '-' and not '/' and not '(' and not ')' and not '（' and not '）'))
            .ToLowerInvariant();
    }

    private static bool RowsEqual(InventoryWorkbookRow left, InventoryWorkbookRow right)
    {
        return string.Equals(left.MaterialCode, right.MaterialCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeText(left.MaterialName), NormalizeText(right.MaterialName), StringComparison.Ordinal)
            && string.Equals(NormalizeText(left.Category), NormalizeText(right.Category), StringComparison.Ordinal)
            && string.Equals(NormalizeText(left.StockUnit), NormalizeText(right.StockUnit), StringComparison.Ordinal)
            && DecimalEquals(left.CurrentStockQty, right.CurrentStockQty)
            && DecimalEquals(left.SafeStockQty, right.SafeStockQty)
            && DecimalEquals(left.Sold7dQty, right.Sold7dQty)
            && DecimalEquals(left.Sold30dQty, right.Sold30dQty)
            && DecimalEquals(left.UnitCost, right.UnitCost)
            && DecimalEquals(left.BraceletSalePrice, right.BraceletSalePrice)
            && DecimalEquals(left.BraceletCostPrice, right.BraceletCostPrice)
            && string.Equals(NormalizeText(left.SupplierName), NormalizeText(right.SupplierName), StringComparison.Ordinal)
            && string.Equals(NormalizeText(left.Remark), NormalizeText(right.Remark), StringComparison.Ordinal)
            && left.Enabled == right.Enabled
            && NullableDateTimeEquals(left.LastRestockedAt, right.LastRestockedAt)
            && NullableDateTimeEquals(left.SourceUpdatedAt, right.SourceUpdatedAt);
    }

    private static bool DecimalEquals(decimal left, decimal right)
    {
        return decimal.Round(left, 4) == decimal.Round(right, 4);
    }

    private static bool NullableDateTimeEquals(DateTime? left, DateTime? right)
    {
        if (!left.HasValue && !right.HasValue)
        {
            return true;
        }

        if (!left.HasValue || !right.HasValue)
        {
            return false;
        }

        return left.Value.ToUniversalTime().Ticks == right.Value.ToUniversalTime().Ticks;
    }

    private static string NormalizeText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool ReadBool(JsonElement element, string name, bool defaultValue)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return defaultValue;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => property.TryGetInt32(out var intValue) ? intValue != 0 : defaultValue,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(property.GetString()) && property.GetString()!.Trim() is not ("0" or "false" or "False"),
            _ => defaultValue
        };
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out var value) ? value : 0,
            JsonValueKind.String => int.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0,
            _ => 0
        };
    }

    private static long ReadLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out var value) ? value : 0,
            JsonValueKind.String => long.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0,
            _ => 0
        };
    }

    private static decimal ReadDecimal(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDecimal(out var value) ? value : 0,
            JsonValueKind.String => decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0,
            _ => 0
        };
    }

    private static decimal? ReadNullableDecimal(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number => property.TryGetDecimal(out var value) ? value : null,
            JsonValueKind.String => decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null,
            _ => null
        };
    }

    private static DateTime? ReadNullableDateTime(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => DateTime.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
                ? parsed
                : null,
            JsonValueKind.Number => property.TryGetInt64(out var value)
                ? DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime
                : null,
            _ => null
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!(element.ValueKind == JsonValueKind.Object
              && element.TryGetProperty(name, out var property)
              && property.ValueKind == JsonValueKind.Array))
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() ?? string.Empty : string.Empty)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long ToUnixMilliseconds(DateTime? value)
    {
        return value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Local)).ToUnixTimeMilliseconds()
            : 0;
    }
}

public sealed class StringNarrationInventoryWorkspaceServiceAdapter : IInventoryWorkspaceService
{
    private readonly IStringNarrationBusinessService _businessService;

    public StringNarrationInventoryWorkspaceServiceAdapter(IStringNarrationBusinessService businessService)
    {
        _businessService = businessService;
    }

    public Task<StringNarrationInventoryManagementDashboardResult> GetDashboardAsync(
        StringNarrationInventoryManagementDashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        return _businessService.GetInventoryManagementDashboardAsync(request, cancellationToken);
    }

    public Task<InventoryImportPreviewResult> PrepareWorkbookImportAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            $"{InventoryGatewayOptions.EndpointEnvironmentVariableName} / {InventoryGatewayOptions.TokenEnvironmentVariableName} 未配置，当前只支持库存只读看板，暂不可导入 Excel。");
    }

    public Task<InventoryImportCommitResult> CommitWorkbookImportAsync(InventoryImportPreviewResult preview, bool writeBackWorkbook, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            $"{InventoryGatewayOptions.EndpointEnvironmentVariableName} / {InventoryGatewayOptions.TokenEnvironmentVariableName} 未配置，当前只支持库存只读看板，暂不可同步到云端。");
    }

    public Task ExportWorkbookAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            $"{InventoryGatewayOptions.EndpointEnvironmentVariableName} / {InventoryGatewayOptions.TokenEnvironmentVariableName} 未配置，当前只支持库存只读看板，暂不可导出 Excel。");
    }
}
