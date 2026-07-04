using Dapper;
using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/price-change-requests")]
public class PriceChangeController : CloudControllerBase
{
    private readonly CommerceCommandService _commandService;
    private readonly PostgresConnectionFactory _connectionFactory;

    public PriceChangeController(
        CommerceCommandService commandService,
        PostgresConnectionFactory connectionFactory,
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _commandService = commandService;
        _connectionFactory = connectionFactory;
    }

    [HttpGet]
    public async Task<ActionResult<PagedList<CloudPriceChangeRequestDto>>> ListAsync(
        Guid workspaceId,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var membership = await GetMembershipAsync();
        if (membership.WorkspaceId != workspaceId) return Forbid();

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;

        var where = @"WHERE r.""WorkspaceId"" = @workspaceId";
        Guid? requesterId = null;
        if (!Permissions.IsAdmin(membership))
        {
            requesterId = UserId;
            where += @" AND r.""RequestedByUserId"" = @requesterId";
        }

        if (!string.IsNullOrWhiteSpace(status))
            where += @" AND r.""Status"" = @status";

        var countSql = $@"
            SELECT COUNT(*) FROM ""CloudPriceChangeRequests"" r
            {where};";

        var itemsSql = $@"
            SELECT
                r.""Id"", r.""WorkspaceId"", r.""ProductId"", r.""CurrentPrice"", r.""ProposedPrice"",
                r.""Reason"", r.""Status"",
                r.""RequestedByUserId"", r.""RequestedAt"", r.""ReviewedByUserId"", r.""ReviewedAt"",
                r.""ReviewNote"", r.""AppliedProductRevision"",
                COALESCE(req.""DisplayName"", '') AS RequestedByDisplayName,
                COALESCE(rev.""DisplayName"", '') AS ReviewedByDisplayName
            FROM ""CloudPriceChangeRequests"" r
            LEFT JOIN ""CloudUsers"" req ON req.""Id"" = r.""RequestedByUserId""
            LEFT JOIN ""CloudUsers"" rev ON rev.""Id"" = r.""ReviewedByUserId""
            {where}
            ORDER BY r.""RequestedAt"" DESC
            LIMIT @pageSize OFFSET @offset;";

        var total = await connection.ExecuteScalarAsync<long>(countSql, new { workspaceId, status, requesterId });
        var rows = await connection.QueryAsync(itemsSql, new { workspaceId, status, requesterId, pageSize, offset });

        var items = rows.Select(r => new CloudPriceChangeRequestDto
        {
            Id = r.Id,
            WorkspaceId = r.WorkspaceId,
            ProductId = r.ProductId,
            CurrentPrice = r.CurrentPrice,
            ProposedPrice = r.ProposedPrice,
            Reason = r.Reason,
            Status = r.Status,
            RequestedByUserId = r.RequestedByUserId,
            RequestedByDisplayName = r.RequestedByDisplayName,
            RequestedAtUtc = r.RequestedAt,
            ReviewedByUserId = r.ReviewedByUserId,
            ReviewedByDisplayName = r.ReviewedByDisplayName,
            ReviewedAtUtc = r.ReviewedAt,
            ReviewNote = r.ReviewNote,
            AppliedProductRevision = r.AppliedProductRevision,
            Revision = r.AppliedProductRevision ?? 0,
            CreatedAtUtc = r.RequestedAt,
            UpdatedAtUtc = r.ReviewedAt ?? r.RequestedAt,
            CreatedByUserId = r.RequestedByUserId,
            UpdatedByUserId = r.ReviewedByUserId,
            Lifecycle = EntityLifecycleStatus.Active
        }).ToList();

        return Ok(new PagedList<CloudPriceChangeRequestDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    [HttpPost]
    public async Task<ActionResult<CloudPriceChangeRequestDto>> CreateAsync(Guid workspaceId, [FromBody] PriceChangeRequestCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.CreatePriceChangeRequestAsync(workspaceId, command);
        return Ok(result.Value);
    }

    [HttpPost("{requestId:guid}/approve")]
    public async Task<ActionResult<CloudPriceChangeRequestDto>> ApproveAsync(Guid workspaceId, Guid requestId, [FromBody] ReviewPriceChangeCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.ApprovePriceChangeRequestAsync(workspaceId, requestId, command);
        return Ok(result.Value);
    }

    [HttpPost("{requestId:guid}/reject")]
    public async Task<ActionResult<CloudPriceChangeRequestDto>> RejectAsync(Guid workspaceId, Guid requestId, [FromBody] ReviewPriceChangeCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.RejectPriceChangeRequestAsync(workspaceId, requestId, command);
        return Ok(result.Value);
    }
}
