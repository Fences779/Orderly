using Dapper;
using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}")]
public class CommerceReadController : CloudControllerBase
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public CommerceReadController(
        PostgresConnectionFactory connectionFactory,
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<CloudDashboardDto>> DashboardAsync(Guid workspaceId)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();

        var canViewCosts = Permissions.CanViewCosts(membership);

        var orderStats = await connection.QueryFirstAsync(
            @"SELECT
                COUNT(*) FILTER (WHERE ""Lifecycle"" = 0) AS TotalOrders,
                COUNT(*) FILTER (WHERE ""SalesStage"" = @completed) AS CompletedOrders,
                COALESCE(SUM(""Total"") FILTER (WHERE ""Lifecycle"" = 0), 0) AS TotalRevenue,
                COALESCE(SUM(""GrossProfit"") FILTER (WHERE ""Lifecycle"" = 0), 0) AS GrossProfit,
                COALESCE(SUM(""ReceivableAmount"") FILTER (WHERE ""Lifecycle"" = 0), 0) AS OutstandingReceivable
            FROM ""CommerceOrders""
            WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL;",
            new { workspaceId, completed = (int)OrderSalesStage.Completed });

        var cashflowStats = await connection.QueryFirstAsync(
            @"SELECT
                COALESCE(SUM(""Amount"") FILTER (WHERE ""Direction"" = @income AND ""SettlementStatus"" = @settled), 0) AS CashInflow,
                COALESCE(SUM(""Amount"") FILTER (WHERE ""Direction"" = @expense AND ""SettlementStatus"" = @settled), 0) AS CashOutflow
            FROM ""CommerceCashFlowEntries""
            WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 0;",
            new { workspaceId, income = (int)CashFlowDirection.Income, expense = (int)CashFlowDirection.Expense, settled = (int)CashFlowSettlementStatus.Settled });

        var customerCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM \"CommerceCustomers\" WHERE \"WorkspaceId\" = @workspaceId AND \"Lifecycle\" = 0;",
            new { workspaceId });

        var lowStockCount = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CommerceInventoryItems""
             WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 0 AND ""QuantityAvailable"" <= ""ReorderThreshold"";",
            new { workspaceId });

        var trend = await connection.QueryAsync(
            @"SELECT DATE(""OrderedAt"" AT TIME ZONE 'UTC') AS DateUtc,
                   COUNT(*) FILTER (WHERE ""SalesStage"" = @completed) AS CompletedOrderCount,
                   COALESCE(SUM(""Total"") FILTER (WHERE ""SalesStage"" = @completed), 0) AS Revenue
            FROM ""CommerceOrders""
            WHERE ""WorkspaceId"" = @workspaceId AND ""OrderedAt"" >= @since AND ""DeletedAt"" IS NULL
            GROUP BY DATE(""OrderedAt"" AT TIME ZONE 'UTC')
            ORDER BY DateUtc;",
            new { workspaceId, completed = (int)OrderSalesStage.Completed, since = DateTime.UtcNow.AddDays(-7) });

        var trendList = trend.Select(t => new CloudDashboardTrendPointDto
        {
            DateUtc = t.DateUtc,
            CompletedOrderCount = (int)t.CompletedOrderCount,
            Revenue = (decimal)t.Revenue
        }).ToList();

        var latestSequence = await GetLatestSequenceAsync(connection, workspaceId);

        return Ok(new CloudDashboardDto
        {
            AsOfUtc = DateTime.UtcNow,
            TotalOrders = (int)orderStats.TotalOrders,
            CompletedOrders = (int)orderStats.CompletedOrders,
            TotalRevenue = (decimal)orderStats.TotalRevenue,
            GrossProfit = canViewCosts ? (decimal?)orderStats.GrossProfit : null,
            OutstandingReceivable = (decimal)orderStats.OutstandingReceivable,
            CashInflow = canViewCosts ? (decimal?)cashflowStats.CashInflow : null,
            CashOutflow = canViewCosts ? (decimal?)cashflowStats.CashOutflow : null,
            NetCashFlow = canViewCosts ? ((decimal)cashflowStats.CashInflow - (decimal)cashflowStats.CashOutflow) : null,
            CustomerCount = customerCount,
            LowStockItemCount = lowStockCount,
            Trend = trendList,
            LatestSequence = latestSequence
        });
    }

    [HttpGet("orders")]
    public async Task<ActionResult<PagedList<CloudOrderDto>>> ListOrdersAsync(
        Guid workspaceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();

        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;

        var where = @"WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0";
        if (!string.IsNullOrWhiteSpace(search))
            where += @" AND (""OrderNo"" ILIKE @search OR COALESCE(""Note"", '') ILIKE @search)";

        var countSql = $"SELECT COUNT(*) FROM \"CommerceOrders\" {where};";
        var itemsSql = $@"
            SELECT * FROM ""CommerceOrders""
            {where}
            ORDER BY ""OrderedAt"" DESC
            LIMIT @pageSize OFFSET @offset;";

        var total = await connection.ExecuteScalarAsync<long>(countSql, new { workspaceId, search = $"%{search}%" });
        var rows = await connection.QueryAsync(itemsSql, new { workspaceId, search = $"%{search}%", pageSize, offset });

        var items = rows.Select(r => MapOrder(r, Permissions.CanViewCosts(membership))).Cast<CloudOrderDto>().ToList();
        return Ok(new PagedList<CloudOrderDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            LatestSequence = await GetLatestSequenceAsync(connection, workspaceId)
        });
    }

    [HttpGet("orders/{orderId:guid}")]
    public async Task<ActionResult<CloudOrderDto>> GetOrderAsync(Guid workspaceId, Guid orderId)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceOrders\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @orderId;",
            new { workspaceId, orderId });
        if (row == null) return NotFound();
        return Ok(MapOrder(row, Permissions.CanViewCosts(membership)));
    }

    [HttpGet("products")]
    public async Task<ActionResult<PagedList<CloudProductDto>>> ListProductsAsync(
        Guid workspaceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();

        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;
        var where = @"WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0";
        if (!string.IsNullOrWhiteSpace(search))
            where += @" AND (""Name"" ILIKE @search OR ""Code"" ILIKE @search)";

        var total = await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM \"CommerceProducts\" {where};", new { workspaceId, search = $"%{search}%" });
        var rows = await connection.QueryAsync($@"
            SELECT * FROM ""CommerceProducts""
            {where}
            ORDER BY ""Name""
            LIMIT @pageSize OFFSET @offset;", new { workspaceId, search = $"%{search}%", pageSize, offset });

        return Ok(new PagedList<CloudProductDto>
        {
            Items = rows.Select(r => MapProduct(r, Permissions.CanViewCosts(membership))).Cast<CloudProductDto>().ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            LatestSequence = await GetLatestSequenceAsync(connection, workspaceId)
        });
    }

    [HttpGet("products/{productId:guid}")]
    public async Task<ActionResult<CloudProductDto>> GetProductAsync(Guid workspaceId, Guid productId)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceProducts\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @productId;",
            new { workspaceId, productId });
        if (row == null) return NotFound();
        return Ok(MapProduct(row, Permissions.CanViewCosts(membership)));
    }

    [HttpGet("inventory/items")]
    public async Task<ActionResult<PagedList<CloudInventoryItemDto>>> ListInventoryAsync(
        Guid workspaceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();

        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;
        var where = @"WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0";
        if (!string.IsNullOrWhiteSpace(search))
            where += @" AND (""Name"" ILIKE @search OR COALESCE(""Sku"", '') ILIKE @search)";

        var total = await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM \"CommerceInventoryItems\" {where};", new { workspaceId, search = $"%{search}%" });
        var rows = await connection.QueryAsync($@"
            SELECT * FROM ""CommerceInventoryItems""
            {where}
            ORDER BY ""Name""
            LIMIT @pageSize OFFSET @offset;", new { workspaceId, search = $"%{search}%", pageSize, offset });

        return Ok(new PagedList<CloudInventoryItemDto>
        {
            Items = rows.Select(r => MapInventoryItem(r, Permissions.CanViewCosts(membership))).Cast<CloudInventoryItemDto>().ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            LatestSequence = await GetLatestSequenceAsync(connection, workspaceId)
        });
    }

    [HttpGet("inventory/items/{itemId:guid}")]
    public async Task<ActionResult<CloudInventoryItemDto>> GetInventoryItemAsync(Guid workspaceId, Guid itemId)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceInventoryItems\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @itemId;",
            new { workspaceId, itemId });
        if (row == null) return NotFound();
        return Ok(MapInventoryItem(row, Permissions.CanViewCosts(membership)));
    }

    [HttpGet("customers")]
    public async Task<ActionResult<PagedList<CloudCustomerDto>>> ListCustomersAsync(
        Guid workspaceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();

        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;
        var where = @"WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0";
        if (!string.IsNullOrWhiteSpace(search))
            where += @" AND (""Name"" ILIKE @search OR COALESCE(""Phone"", '') ILIKE @search OR COALESCE(""WeChat"", '') ILIKE @search)";

        var total = await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM \"CommerceCustomers\" {where};", new { workspaceId, search = $"%{search}%" });
        var rows = await connection.QueryAsync($@"
            SELECT * FROM ""CommerceCustomers""
            {where}
            ORDER BY ""Name""
            LIMIT @pageSize OFFSET @offset;", new { workspaceId, search = $"%{search}%", pageSize, offset });

        return Ok(new PagedList<CloudCustomerDto>
        {
            Items = rows.Select(MapCustomer).Cast<CloudCustomerDto>().ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            LatestSequence = await GetLatestSequenceAsync(connection, workspaceId)
        });
    }

    [HttpGet("customers/{customerId:guid}")]
    public async Task<ActionResult<CloudCustomerDto>> GetCustomerAsync(Guid workspaceId, Guid customerId)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceCustomers\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @customerId;",
            new { workspaceId, customerId });
        if (row == null) return NotFound();
        return Ok(MapCustomer(row));
    }

    [HttpGet("cashflow/summary")]
    public async Task<ActionResult<object>> CashFlowSummaryAsync(Guid workspaceId)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        if (!Permissions.CanViewCosts(membership)) return Forbid();

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var summary = await connection.QueryFirstOrDefaultAsync(
            @"SELECT
                COALESCE(SUM(""Amount"") FILTER (WHERE ""Direction"" = @income AND ""SettlementStatus"" = @settled), 0) AS RealizedIncome,
                COALESCE(SUM(""Amount"") FILTER (WHERE ""Direction"" = @expense AND ""SettlementStatus"" = @settled), 0) AS RealizedExpense,
                COALESCE(SUM(""Amount"" - ""SettledAmount"") FILTER (WHERE ""Direction"" = @income), 0) AS OutstandingReceivable,
                COALESCE(SUM(""Amount"" - ""SettledAmount"") FILTER (WHERE ""Direction"" = @expense), 0) AS OutstandingPayable
            FROM ""CommerceCashFlowEntries""
            WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 0;",
            new { workspaceId, income = (int)CashFlowDirection.Income, expense = (int)CashFlowDirection.Expense, settled = (int)CashFlowSettlementStatus.Settled });

        return Ok(new
        {
            RealizedIncome = (decimal)summary.RealizedIncome,
            RealizedExpense = (decimal)summary.RealizedExpense,
            NetCashFlow = (decimal)summary.RealizedIncome - (decimal)summary.RealizedExpense,
            OutstandingReceivable = (decimal)summary.OutstandingReceivable,
            OutstandingPayable = (decimal)summary.OutstandingPayable,
            LatestSequence = await GetLatestSequenceAsync(connection, workspaceId)
        });
    }

    [HttpGet("cashflow/entries")]
    public async Task<ActionResult<PagedList<CloudCashFlowEntryDto>>> ListCashFlowEntriesAsync(
        Guid workspaceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        if (!Permissions.CanViewCosts(membership)) return Forbid();

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM \"CommerceCashFlowEntries\" WHERE \"WorkspaceId\" = @workspaceId AND \"Lifecycle\" = 0;",
            new { workspaceId });
        var rows = await connection.QueryAsync(
            @"SELECT * FROM ""CommerceCashFlowEntries""
             WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 0
             ORDER BY ""OccurredAt"" DESC
             LIMIT @pageSize OFFSET @offset;",
            new { workspaceId, pageSize, offset });

        return Ok(new PagedList<CloudCashFlowEntryDto>
        {
            Items = rows.Select(MapCashFlowEntry).Cast<CloudCashFlowEntryDto>().ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            LatestSequence = await GetLatestSequenceAsync(connection, workspaceId)
        });
    }

    [HttpGet("cashflow/entries/{entryId:guid}")]
    public async Task<ActionResult<CloudCashFlowEntryDto>> GetCashFlowEntryAsync(Guid workspaceId, Guid entryId)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        if (!Permissions.CanViewCosts(membership)) return Forbid();

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceCashFlowEntries\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @entryId;",
            new { workspaceId, entryId });
        if (row == null) return NotFound();
        return Ok(MapCashFlowEntry(row));
    }

    [HttpGet("insights")]
    public async Task<ActionResult<PagedList<CloudBusinessInsightDto>>> ListInsightsAsync(
        Guid workspaceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        if (!Permissions.CanViewCosts(membership)) return Forbid();

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM \"CommerceBusinessInsights\" WHERE \"WorkspaceId\" = @workspaceId AND \"Lifecycle\" = 0;",
            new { workspaceId });
        var rows = await connection.QueryAsync(
            @"SELECT * FROM ""CommerceBusinessInsights""
             WHERE ""WorkspaceId"" = @workspaceId AND ""Lifecycle"" = 0
             ORDER BY ""GeneratedAt"" DESC
             LIMIT @pageSize OFFSET @offset;",
            new { workspaceId, pageSize, offset });

        return Ok(new PagedList<CloudBusinessInsightDto>
        {
            Items = rows.Select(MapInsight).Cast<CloudBusinessInsightDto>().ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            LatestSequence = await GetLatestSequenceAsync(connection, workspaceId)
        });
    }

    [HttpGet("business-tasks")]
    public async Task<ActionResult<PagedList<CloudBusinessTaskDto>>> ListBusinessTasksAsync(
        Guid workspaceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();

        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;
        var where = @"WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0";
        if (!string.IsNullOrWhiteSpace(search))
            where += @" AND (""Title"" ILIKE @search OR COALESCE(""Description"", '') ILIKE @search)";

        var total = await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM \"CommerceBusinessTasks\" {where};", new { workspaceId, search = $"%{search}%" });
        var rows = await connection.QueryAsync($@"
            SELECT * FROM ""CommerceBusinessTasks""
            {where}
            ORDER BY ""DueDate"" NULLS LAST, ""CreatedAt"" DESC
            LIMIT @pageSize OFFSET @offset;", new { workspaceId, search = $"%{search}%", pageSize, offset });

        return Ok(new PagedList<CloudBusinessTaskDto>
        {
            Items = rows.Select(MapBusinessTask).Cast<CloudBusinessTaskDto>().ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            LatestSequence = await GetLatestSequenceAsync(connection, workspaceId)
        });
    }

    [HttpGet("business-tasks/{taskId:guid}")]
    public async Task<ActionResult<CloudBusinessTaskDto>> GetBusinessTaskAsync(Guid workspaceId, Guid taskId)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceBusinessTasks\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @taskId AND \"DeletedAt\" IS NULL AND \"Lifecycle\" = 0;",
            new { workspaceId, taskId });
        if (row == null) return NotFound();
        return Ok(MapBusinessTask(row));
    }

    [HttpGet("archive/{entityType}")]
    public async Task<ActionResult<object>> ListArchiveAsync(Guid workspaceId, string entityType, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        if (!Permissions.IsAdmin(membership)) return Forbid();

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;

        var table = entityType.ToLowerInvariant() switch
        {
            "order" or "orders" => "CommerceOrders",
            "product" or "products" => "CommerceProducts",
            "inventoryitem" or "inventory" => "CommerceInventoryItems",
            "customer" or "customers" => "CommerceCustomers",
            "cashflow" => "CommerceCashFlowEntries",
            "task" or "tasks" => "CommerceBusinessTasks",
            _ => null
        };
        if (table == null) return BadRequest(new { Error = "Unsupported entity type." });

        var total = await connection.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM \"{table}\" WHERE \"WorkspaceId\" = @workspaceId AND \"Lifecycle\" = 1;",
            new { workspaceId });
        var rows = await connection.QueryAsync(
            $"SELECT * FROM \"{table}\" WHERE \"WorkspaceId\" = @workspaceId AND \"Lifecycle\" = 1 ORDER BY \"UpdatedAt\" DESC LIMIT @pageSize OFFSET @offset;",
            new { workspaceId, pageSize, offset });

        return Ok(new { Items = rows, Page = page, PageSize = pageSize, TotalCount = total });
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<PagedList<CloudAuditLogDto>>> ListAuditLogsAsync(
        Guid workspaceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var membership = await GetMembershipAsync();
        if (!Permissions.IsAdmin(membership)) return Forbid();

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM \"CloudAuditLogs\" WHERE \"WorkspaceId\" = @workspaceId;",
            new { workspaceId });
        var rows = await connection.QueryAsync(
            @"SELECT * FROM ""CloudAuditLogs""
             WHERE ""WorkspaceId"" = @workspaceId
             ORDER BY ""OccurredAt"" DESC
             LIMIT @pageSize OFFSET @offset;",
            new { workspaceId, pageSize, offset });

        return Ok(new PagedList<CloudAuditLogDto>
        {
            Items = rows.Select(r => new CloudAuditLogDto
            {
                Id = r.Id,
                WorkspaceId = r.WorkspaceId,
                ActorUserId = r.ActorUserId,
                ActorDisplayName = r.ActorDisplayName,
                ActorRole = r.ActorRole,
                Action = r.Action,
                EntityType = r.EntityType,
                EntityId = r.EntityId,
                BeforeJson = r.BeforeJson,
                AfterJson = r.AfterJson,
                Reason = r.Reason,
                ClientRequestId = r.ClientRequestId,
                OccurredAtUtc = r.OccurredAt
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    private static async Task<long> GetLatestSequenceAsync(System.Data.Common.DbConnection connection, Guid workspaceId)
    {
        var sequence = await connection.ExecuteScalarAsync<long?>(
            "SELECT \"LastSequence\" FROM \"CloudWorkspaceSyncState\" WHERE \"WorkspaceId\" = @workspaceId;",
            new { workspaceId });
        return sequence ?? 0;
    }

    private static CloudOrderDto MapOrder(dynamic r, bool canViewCosts) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        OrderNo = r.OrderNo,
        CustomerId = r.CustomerId,
        SalesStage = (OrderSalesStage)(int)r.SalesStage,
        PaymentStage = (OrderPaymentStage)(int)r.PaymentStage,
        FulfillmentStage = (OrderFulfillmentStage)(int)r.FulfillmentStage,
        Subtotal = r.Subtotal,
        Total = r.Total,
        Cost = canViewCosts ? (decimal?)r.Cost : null,
        GrossProfit = canViewCosts ? (decimal?)r.GrossProfit : null,
        GrossMargin = canViewCosts ? (decimal?)r.GrossMargin : null,
        PaidAmount = r.PaidAmount,
        ReceivableAmount = r.ReceivableAmount,
        OrderedAtUtc = r.OrderedAt,
        Note = r.Note,
        AssignedToUserId = r.AssignedToUserId,
        ArchivedByUserId = r.ArchivedByUserId,
        ArchiveReason = r.ArchiveReason
    };

    private static CloudProductDto MapProduct(dynamic r, bool canViewCosts) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Name = r.Name,
        Code = r.Code,
        ProductType = (ProductType)(int)r.ProductType,
        Description = r.Description,
        DefaultUnitId = r.DefaultUnitId,
        SupplierId = r.SupplierId,
        DefaultPrice = r.DefaultPrice,
        DefaultCost = canViewCosts ? (decimal?)r.DefaultCost : null
    };

    private static CloudInventoryItemDto MapInventoryItem(dynamic r, bool canViewCosts) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Name = r.Name,
        Sku = r.Sku,
        ProductId = r.ProductId,
        ProductVariantId = r.ProductVariantId,
        UnitId = r.UnitId,
        QuantityAvailable = r.QuantityAvailable,
        ReorderThreshold = r.ReorderThreshold,
        UnitCost = canViewCosts ? (decimal?)r.UnitCost : null
    };

    private static CloudCustomerDto MapCustomer(dynamic r) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Name = r.Name,
        Phone = r.Phone,
        WeChat = r.WeChat,
        Email = r.Email,
        LastOrderAtUtc = r.LastOrderAt,
        CompletedOrderCount = r.CompletedOrderCount,
        TotalSpend = r.TotalSpend,
        AssignedToUserId = r.AssignedToUserId
    };

    private static CloudCashFlowEntryDto MapCashFlowEntry(dynamic r) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Direction = (CashFlowDirection)(int)r.Direction,
        Amount = r.Amount,
        SettledAmount = r.SettledAmount,
        SettlementStatus = (CashFlowSettlementStatus)(int)r.SettlementStatus,
        OccurredAtUtc = r.OccurredAt,
        DueDateUtc = r.DueDate,
        CategoryName = r.CategoryName,
        OrderId = r.OrderId,
        PaymentRecordId = r.PaymentRecordId,
        BusinessKey = r.BusinessKey
    };

    private static CloudBusinessInsightDto MapInsight(dynamic r) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Severity = (InsightSeverity)(int)r.Severity,
        Title = r.Title,
        Message = r.Message,
        Category = r.Category,
        IsAcknowledged = r.IsAcknowledged,
        GeneratedAtUtc = r.GeneratedAt,
        BusinessKey = r.BusinessKey
    };

    private static CloudBusinessTaskDto MapBusinessTask(dynamic r) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Title = r.Title,
        Description = r.Description,
        Status = (Orderly.Core.Commerce.TaskStatus)(int)r.Status,
        DueDateUtc = r.DueDate,
        CompletedAtUtc = r.CompletedAt,
        CustomerId = r.CustomerId,
        OrderId = r.OrderId,
        AssignedToUserId = r.AssignedToUserId
    };
}
