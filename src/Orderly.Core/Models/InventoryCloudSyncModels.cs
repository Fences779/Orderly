using System.Globalization;

namespace Orderly.Core.Models;

public sealed class InventoryWorkbookRow
{
    public int RowNumber { get; set; }

    public string MaterialCode { get; set; } = string.Empty;

    public string MaterialName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string StockUnit { get; set; } = "件";

    public decimal CurrentStockQty { get; set; }

    public decimal SafeStockQty { get; set; }

    public decimal Sold7dQty { get; set; }

    public decimal Sold30dQty { get; set; }

    public decimal UnitCost { get; set; }

    public decimal BraceletSalePrice { get; set; }

    public decimal BraceletCostPrice { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public DateTime? LastRestockedAt { get; set; }

    public DateTime? SourceUpdatedAt { get; set; }

    public decimal Sold7dRatio => CurrentStockQty > 0 ? Sold7dQty / CurrentStockQty : 0;

    public decimal Sold30dRatio => CurrentStockQty > 0 ? Sold30dQty / CurrentStockQty : 0;

    public bool IsLowStock => SafeStockQty > 0 && CurrentStockQty <= SafeStockQty;
}

public sealed class InventoryImportRowError
{
    public int RowNumber { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class InventoryImportPreviewResult
{
    public string SourcePath { get; set; } = string.Empty;

    public string SourceFileHash { get; set; } = string.Empty;

    public IReadOnlyList<InventoryWorkbookRow> Rows { get; set; } = [];

    public IReadOnlyList<InventoryImportRowError> Errors { get; set; } = [];

    public int InsertedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int UnchangedCount { get; set; }

    public int SkippedCount { get; set; }

    public int LowStockCount { get; set; }

    public int DisabledCount { get; set; }

    public bool CanCommit => Errors.Count == 0 && Rows.Count > 0;

    public string SourceFileName => string.IsNullOrWhiteSpace(SourcePath) ? "未命名文件" : Path.GetFileName(SourcePath);

    public string SummaryText => string.Create(
        CultureInfo.InvariantCulture,
        $"共 {Rows.Count} 行，新增 {InsertedCount}，更新 {UpdatedCount}，未变化 {UnchangedCount}，跳过 {SkippedCount}，低库存 {LowStockCount}，停用 {DisabledCount}");
}

public sealed class InventoryImportCommitResult
{
    public string BatchNo { get; set; } = string.Empty;

    public int TotalRows { get; set; }

    public int InsertedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int UnchangedCount { get; set; }

    public int SkippedCount { get; set; }

    public long UpdatedAt { get; set; }

    public string WorkbookPath { get; set; } = string.Empty;

    public string BackupPath { get; set; } = string.Empty;
}
