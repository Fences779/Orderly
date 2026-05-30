using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed partial class CloudInventoryWorkspaceService : IInventoryWorkspaceService
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
