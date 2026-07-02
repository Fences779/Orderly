using System.Data;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Server.Mapping;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    private async Task<dynamic?> GetProductRowAsync(Guid workspaceId, Guid productId, CancellationToken cancellationToken)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceProducts\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @productId;",
            new { workspaceId, productId });
    }

    private async Task<CloudPriceChangeRequestDto> LoadPriceChangeRequestDtoAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid requestId, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            @"SELECT r.*,
                     requester.""DisplayName"" AS ""RequestedByDisplayName"",
                     reviewer.""DisplayName"" AS ""ReviewedByDisplayName""
              FROM ""CloudPriceChangeRequests"" r
              LEFT JOIN ""CloudUsers"" requester ON requester.""Id"" = r.""RequestedByUserId""
              LEFT JOIN ""CloudUsers"" reviewer ON reviewer.""Id"" = r.""ReviewedByUserId""
              WHERE r.""Id"" = @requestId AND r.""WorkspaceId"" = @workspaceId;",
            new { requestId, workspaceId },
            transaction)
            ?? throw new InvalidOperationException($"改价申请 {requestId} 不存在。");

        return new CloudPriceChangeRequestDto
        {
            Id = row.Id,
            WorkspaceId = row.WorkspaceId,
            ProductId = row.ProductId,
            CurrentPrice = row.CurrentPrice,
            ProposedPrice = row.ProposedPrice,
            Reason = row.Reason,
            Status = row.Status,
            RequestedByUserId = row.RequestedByUserId,
            RequestedByDisplayName = row.RequestedByDisplayName ?? string.Empty,
            RequestedAtUtc = row.RequestedAt,
            ReviewedByUserId = row.ReviewedByUserId,
            ReviewedByDisplayName = row.ReviewedByDisplayName,
            ReviewedAtUtc = row.ReviewedAt,
            ReviewNote = row.ReviewNote,
            AppliedProductRevision = row.AppliedProductRevision,
            Revision = 0,
            CreatedAtUtc = row.RequestedAt,
            UpdatedAtUtc = row.ReviewedAt ?? row.RequestedAt,
            Lifecycle = EntityLifecycleStatus.Active
        };
    }

    private async Task<CloudProductDto> LoadProductDtoAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid productId, CloudWorkspaceMemberRecord membership, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceProducts\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @productId;",
            new { workspaceId, productId },
            transaction)
            ?? throw new InvalidOperationException($"商品 {productId} 不存在。");

        return CommerceDtoMapper.ToProductDto(row, _permissions.CanViewCosts(membership));
    }
}
