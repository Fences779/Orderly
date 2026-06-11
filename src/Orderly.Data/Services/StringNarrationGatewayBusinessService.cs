using System.Text.Json;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed partial class StringNarrationGatewayBusinessService : IStringNarrationBusinessService
{
    private const string InventoryListAction = "inventoryList";
    private const string InventoryManagementDashboardAction = "inventoryManagementDashboard";
    private const string CashflowListAction = "cashflowList";
    private const string CashflowHealthDashboardAction = "cashflowHealthDashboard";

    private readonly StringNarrationGatewayClient _client;

    public StringNarrationGatewayBusinessService(StringNarrationGatewayClient client)
    {
        _client = client;
    }

    public async Task<StringNarrationInventoryListResult> GetInventoryAsync(StringNarrationInventoryQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            query ??= new StringNarrationInventoryQuery();
            var root = await _client.InvokeAsync(InventoryListAction, NormalizeInventoryQuery(query), cancellationToken);
            var payload = GetPayloadRoot(root);
            var items = ParseInventoryItems(payload);
            var movements = ParseInventoryMovements(payload);
            return new StringNarrationInventoryListResult
            {
                Items = items,
                RecentMovements = movements,
                Total = ReadInt(payload, "total", "count"),
                UpdatedAt = ReadLong(payload, "updatedAt", "calculatedAt", "syncedAt")
            };
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(InventoryListAction, ex);
        }
    }

    public async Task<StringNarrationInventoryManagementDashboardResult> GetInventoryManagementDashboardAsync(
        StringNarrationInventoryManagementDashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new StringNarrationInventoryManagementDashboardRequest();
            var gatewayRequest = NormalizeInventoryManagementDashboardRequest(request);
            var requestedStatus = NormalizeInventoryDashboardStatusValue(gatewayRequest.Status);
            var shouldFilterClientSide = RequiresClientSideInventoryStatusFilter(requestedStatus);
            if (shouldFilterClientSide)
            {
                gatewayRequest.Status = "all";
            }

            var root = await _client.InvokeAsync(InventoryManagementDashboardAction, gatewayRequest, cancellationToken);
            var payload = GetPayloadRoot(root);
            ValidateInventoryManagementDashboardPayload(payload);
            var items = ParseInventoryManagementDashboardItems(payload);
            if (shouldFilterClientSide)
            {
                items = ApplyInventoryStatusFilter(items, requestedStatus);
            }

            return new StringNarrationInventoryManagementDashboardResult
            {
                UpdatedAt = ReadLong(payload, "updatedAt", "calculatedAt", "syncedAt"),
                DataAvailability = ParseDataAvailability(payload),
                Summary = ParseInventoryManagementDashboardSummary(payload, items),
                FilterOptions = ParseInventoryManagementDashboardFilterOptions(payload),
                Items = items,
                PageInfo = ParseInventoryManagementDashboardPageInfo(
                    payload,
                    shouldFilterClientSide ? items.Count : null,
                    gatewayRequest.Page,
                    gatewayRequest.PageSize)
            };
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(InventoryManagementDashboardAction, ex);
        }
    }

    public async Task<StringNarrationCashflowListResult> GetCashflowAsync(StringNarrationCashflowQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            query ??= new StringNarrationCashflowQuery();
            var root = await _client.InvokeAsync(CashflowListAction, NormalizeCashflowQuery(query), cancellationToken);
            var payload = GetPayloadRoot(root);
            var entries = ParseCashflowEntries(payload);
            var summary = TryGet(payload, "summary", out var summaryElement) && summaryElement.ValueKind == JsonValueKind.Object
                ? ParseCashflowSummary(summaryElement, entries)
                : BuildCashflowSummary(entries);

            return new StringNarrationCashflowListResult
            {
                Entries = entries,
                Summary = summary,
                Total = ReadInt(payload, "total", "count"),
                UpdatedAt = ReadLong(payload, "updatedAt", "calculatedAt", "syncedAt")
            };
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(CashflowListAction, ex);
        }
    }

    public async Task<StringNarrationCashflowHealthDashboardResult> GetCashflowHealthDashboardAsync(
        StringNarrationCashflowHealthDashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new StringNarrationCashflowHealthDashboardRequest();
            var root = await _client.InvokeAsync(CashflowHealthDashboardAction, NormalizeCashflowHealthDashboardRequest(request), cancellationToken);
            var payload = GetPayloadRoot(root);
            ValidateCashflowHealthDashboardPayload(payload);
            return ParseCashflowHealthDashboard(payload);
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(CashflowHealthDashboardAction, ex);
        }
    }

    private static InvalidOperationException WrapActionException(string action, InvalidOperationException ex)
    {
        return ex.Message.Contains($"action={action}", StringComparison.OrdinalIgnoreCase)
            ? ex
            : new InvalidOperationException($"调用串述 adminPcGateway action={action} 失败：{ex.Message}", ex);
    }

    private static StringNarrationInventoryQuery NormalizeInventoryQuery(StringNarrationInventoryQuery query)
    {
        return new StringNarrationInventoryQuery
        {
            Page = StringNarrationGatewayInputSafety.NormalizePage(query.Page),
            PageSize = StringNarrationGatewayInputSafety.NormalizePageSize(query.PageSize, fallback: 100),
            Keyword = StringNarrationGatewayInputSafety.NormalizeKeyword(query.Keyword, "keyword"),
            Category = StringNarrationGatewayInputSafety.NormalizeFilter(query.Category, "category"),
            IncludeDisabled = query.IncludeDisabled,
            LowStockOnly = query.LowStockOnly
        };
    }

    private static StringNarrationInventoryManagementDashboardRequest NormalizeInventoryManagementDashboardRequest(
        StringNarrationInventoryManagementDashboardRequest request)
    {
        return new StringNarrationInventoryManagementDashboardRequest
        {
            Keyword = StringNarrationGatewayInputSafety.NormalizeKeyword(request.Keyword, "keyword"),
            Category = StringNarrationGatewayInputSafety.NormalizeFilter(request.Category, "category"),
            Status = StringNarrationGatewayInputSafety.NormalizeFilter(request.Status, "status"),
            SortBy = StringNarrationGatewayInputSafety.NormalizeFilter(request.SortBy, "sortBy"),
            SortDirection = StringNarrationGatewayInputSafety.NormalizeFilter(request.SortDirection, "sortDirection"),
            Page = StringNarrationGatewayInputSafety.NormalizePage(request.Page),
            PageSize = StringNarrationGatewayInputSafety.NormalizePageSize(request.PageSize, fallback: 10)
        };
    }

    private static StringNarrationCashflowQuery NormalizeCashflowQuery(StringNarrationCashflowQuery query)
    {
        return new StringNarrationCashflowQuery
        {
            Page = StringNarrationGatewayInputSafety.NormalizePage(query.Page),
            PageSize = StringNarrationGatewayInputSafety.NormalizePageSize(query.PageSize, fallback: 100),
            Keyword = StringNarrationGatewayInputSafety.NormalizeKeyword(query.Keyword, "keyword"),
            Direction = StringNarrationGatewayInputSafety.NormalizeFilter(query.Direction, "direction"),
            Category = StringNarrationGatewayInputSafety.NormalizeFilter(query.Category, "category"),
            StartAt = StringNarrationGatewayInputSafety.NormalizeTimestamp(query.StartAt),
            EndAt = StringNarrationGatewayInputSafety.NormalizeTimestamp(query.EndAt)
        };
    }

    private static StringNarrationCashflowHealthDashboardRequest NormalizeCashflowHealthDashboardRequest(
        StringNarrationCashflowHealthDashboardRequest request)
    {
        return new StringNarrationCashflowHealthDashboardRequest
        {
            Range = StringNarrationGatewayInputSafety.NormalizeFilter(request.Range, "range"),
            StartAt = StringNarrationGatewayInputSafety.NormalizeTimestamp(request.StartAt),
            EndAt = StringNarrationGatewayInputSafety.NormalizeTimestamp(request.EndAt)
        };
    }

    private static List<StringNarrationInventoryManagementDashboardItem> ApplyInventoryStatusFilter(
        IReadOnlyList<StringNarrationInventoryManagementDashboardItem> items,
        string status)
    {
        return items
            .Where(item => string.Equals(item.Status, status, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool RequiresClientSideInventoryStatusFilter(string status)
    {
        return string.Equals(status, "fast_selling", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "low_selling", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInventoryDashboardStatusValue(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? "all" : status.Trim();
    }

    private static string NormalizeInventoryDashboardStatus(
        string rawStatus,
        decimal currentStockQty,
        decimal? safeStockSuggestedQty,
        decimal sold30dQty,
        decimal sold30dRatio)
    {
        var normalized = NormalizeInventoryDashboardStatusValue(rawStatus);
        if (string.Equals(normalized, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return "disabled";
        }

        if (safeStockSuggestedQty is > 0 && currentStockQty <= safeStockSuggestedQty.Value)
        {
            return "low_stock";
        }

        if (string.Equals(normalized, "low_stock", StringComparison.OrdinalIgnoreCase))
        {
            return "low_stock";
        }

        if (IsFastSellingRatio(sold30dRatio))
        {
            return "fast_selling";
        }

        if (sold30dQty <= 0)
        {
            return "low_selling";
        }

        return string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase) ? "normal" : normalized;
    }

    private static bool IsFastSellingRatio(decimal ratio)
    {
        return ratio > 1m ? ratio >= 50m : ratio >= 0.5m;
    }

    private static string BuildInventoryDashboardStatusLabel(string status)
    {
        return status switch
        {
            "fast_selling" => "动销偏快",
            "low_selling" => "低动销",
            "low_stock" => "低库存",
            "disabled" => "停用",
            _ => "正常"
        };
    }

    private static StringNarrationBusinessDataAvailabilityItem ResolveCashflowAvailabilityByType(
        StringNarrationBusinessDataAvailability dataAvailability,
        string type)
    {
        return type switch
        {
            "receivable" => dataAvailability.Receivable,
            "payable" => dataAvailability.Payable,
            _ => new StringNarrationBusinessDataAvailabilityItem()
        };
    }

    private static string BuildUpcomingCashItemLabel(string label, string type, bool isCompatPlaceholder)
    {
        var resolvedLabel = !string.IsNullOrWhiteSpace(label)
            ? label.Trim()
            : type switch
            {
                "receivable" => "待收款",
                "payable" => "待付款",
                _ => "未命名"
            };

        return isCompatPlaceholder ? $"{resolvedLabel}（兼容占位）" : resolvedLabel;
    }

    private static string BuildUpcomingCashItemNote(
        string rawNote,
        StringNarrationBusinessDataAvailabilityItem availability,
        bool isCompatPlaceholder)
    {
        if (isCompatPlaceholder)
        {
            var reason = string.IsNullOrWhiteSpace(availability.Reason)
                ? "当前 action 仅返回兼容占位数据。"
                : availability.Reason.Trim();
            return availability.IsUnavailable ? $"字段未接入：{reason}" : $"兼容占位：{reason}";
        }

        return string.IsNullOrWhiteSpace(rawNote) ? "暂无说明" : rawNote.Trim();
    }
}
