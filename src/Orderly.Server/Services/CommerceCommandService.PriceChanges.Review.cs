using System.Data;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Contracts.Realtime;
using Orderly.Server.Mapping;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task<CommandResult<CloudPriceChangeRequestDto>> ApprovePriceChangeRequestAsync(Guid workspaceId, Guid requestId, ReviewPriceChangeCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.CanApprovePriceChange(membership))
            throw new UnauthorizedAccessException("没有审批改价申请的权限。");

        return await ExecuteWithIdempotencyResultAsync<ReviewPriceChangeCommand, CloudPriceChangeRequestDto>(
            workspaceId,
            "price-change-request:approve",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                var productSequence = sequence;
                var requestRow = await connection.QueryFirstOrDefaultAsync(
                    @"SELECT * FROM ""CloudPriceChangeRequests""
                     WHERE ""Id"" = @requestId AND ""WorkspaceId"" = @workspaceId
                     FOR UPDATE;",
                    new { requestId, workspaceId },
                    transaction)
                    ?? throw new InvalidOperationException($"改价申请 {requestId} 不存在。");

                if (!string.Equals((string)requestRow.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("只能审批待处理状态的改价申请。");

                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceProducts", (Guid)requestRow.ProductId, command.ExpectedRevision, ct);

                var now = DateTime.UtcNow;
                var productBefore = await LoadProductDtoAsync(connection, transaction, workspaceId, (Guid)requestRow.ProductId, membership, ct);
                var productBeforeJson = await SnapshotJsonAsync(productBefore);

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceProducts""
                     SET ""DefaultPrice"" = @proposedPrice,
                         ""Revision"" = ""Revision"" + 1,
                         ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""LastChangeSequence"" = @productSequence
                     WHERE ""Id"" = @productId;",
                    new
                    {
                        proposedPrice = (decimal)requestRow.ProposedPrice,
                        now,
                        updatedBy = userId,
                        productSequence,
                        productId = (Guid)requestRow.ProductId
                    },
                    transaction);

                await connection.ExecuteAsync(
                    @"UPDATE ""CloudPriceChangeRequests""
                     SET ""Status"" = @status,
                         ""ReviewedByUserId"" = @reviewedBy,
                         ""ReviewedAt"" = @reviewedAt,
                         ""ReviewNote"" = @reviewNote,
                         ""AppliedProductRevision"" = @appliedRevision
                     WHERE ""Id"" = @requestId;",
                    new
                    {
                        status = "Approved",
                        reviewedBy = userId,
                        reviewedAt = now,
                        reviewNote = command.ReviewNote,
                        appliedRevision = productBefore.Revision + 1,
                        requestId
                    },
                    transaction);

                var productAfter = await LoadProductDtoAsync(connection, transaction, workspaceId, (Guid)requestRow.ProductId, membership, ct);
                var productAfterJson = await SnapshotJsonAsync(productAfter);
                await AuditAsync(connection, transaction, workspaceId, "PriceChangeApproved", EntityType.Product, (Guid)requestRow.ProductId, productBeforeJson, productAfterJson, command.ReviewNote, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, productSequence, EntityType.Product, (Guid)requestRow.ProductId, "priceChanged", productAfter.Revision);

                collector.Add(RealtimeEvent.EntityUpdated, new RealtimeEventPayload
                {
                    WorkspaceId = workspaceId,
                    EntityType = EntityType.Product,
                    EntityId = (Guid)requestRow.ProductId,
                    Sequence = productSequence,
                    ActorUserId = userId,
                    ActorDisplayName = _currentUser.DisplayName ?? string.Empty,
                    OccurredAtUtc = now,
                    Action = "priceChanged",
                    Revision = productAfter.Revision
                });

                var dto = await LoadPriceChangeRequestDtoAsync(connection, transaction, workspaceId, requestId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "PriceChangeRequestApproved", EntityType.PriceChangeRequest, requestId, null, afterJson, command.ReviewNote, command.ClientRequestId, collector);
                var requestSequence = await AllocateSequenceAsync(connection, transaction, workspaceId);
                await RecordChangeAsync(connection, transaction, workspaceId, requestSequence, EntityType.PriceChangeRequest, requestId, "approved", 0);

                return new CommandExecutionResult<CloudPriceChangeRequestDto>(dto, EntityType.PriceChangeRequest, requestId, requestSequence);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudPriceChangeRequestDto>> RejectPriceChangeRequestAsync(Guid workspaceId, Guid requestId, ReviewPriceChangeCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.CanApprovePriceChange(membership))
            throw new UnauthorizedAccessException("没有审批改价申请的权限。");

        return await ExecuteWithIdempotencyAsync<ReviewPriceChangeCommand, CloudPriceChangeRequestDto>(
            workspaceId,
            "price-change-request:reject",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                var requestRow = await connection.QueryFirstOrDefaultAsync(
                    @"SELECT * FROM ""CloudPriceChangeRequests""
                     WHERE ""Id"" = @requestId AND ""WorkspaceId"" = @workspaceId
                     FOR UPDATE;",
                    new { requestId, workspaceId },
                    transaction)
                    ?? throw new InvalidOperationException($"改价申请 {requestId} 不存在。");

                if (!string.Equals((string)requestRow.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("只能驳回待处理状态的改价申请。");

                var now = DateTime.UtcNow;
                await connection.ExecuteAsync(
                    @"UPDATE ""CloudPriceChangeRequests""
                     SET ""Status"" = @status,
                         ""ReviewedByUserId"" = @reviewedBy,
                         ""ReviewedAt"" = @reviewedAt,
                         ""ReviewNote"" = @reviewNote
                     WHERE ""Id"" = @requestId;",
                    new
                    {
                        status = "Rejected",
                        reviewedBy = userId,
                        reviewedAt = now,
                        reviewNote = command.ReviewNote,
                        requestId
                    },
                    transaction);

                var dto = await LoadPriceChangeRequestDtoAsync(connection, transaction, workspaceId, requestId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "PriceChangeRequestRejected", EntityType.PriceChangeRequest, requestId, null, afterJson, command.ReviewNote, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.PriceChangeRequest, requestId, "rejected", 0);

                return (dto, EntityType.PriceChangeRequest, requestId);
            },
            cancellationToken);
    }
}
