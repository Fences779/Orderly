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
    public async Task<CommandResult<CloudPriceChangeRequestDto>> CreatePriceChangeRequestAsync(Guid workspaceId, PriceChangeRequestCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        var productRow = await GetProductRowAsync(workspaceId, command.ProductId, cancellationToken)
            ?? throw new InvalidOperationException($"商品 {command.ProductId} 不存在。");

        return await ExecuteWithIdempotencyAsync<PriceChangeRequestCommand, CloudPriceChangeRequestDto>(
            workspaceId,
            "price-change-request:create",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                var now = DateTime.UtcNow;
                var requestId = Guid.NewGuid();
                var currentPrice = (decimal)productRow.DefaultPrice;

                await connection.ExecuteAsync(
                    @"INSERT INTO ""CloudPriceChangeRequests"" (
                        ""Id"", ""WorkspaceId"", ""ProductId"", ""CurrentPrice"", ""ProposedPrice"",
                        ""Reason"", ""Status"", ""RequestedByUserId"", ""RequestedAt"")
                    VALUES (
                        @id, @workspaceId, @productId, @currentPrice, @proposedPrice,
                        @reason, @status, @requestedByUserId, @requestedAt);",
                    new
                    {
                        id = requestId,
                        workspaceId,
                        command.ProductId,
                        currentPrice,
                        proposedPrice = RoundMoney(command.ProposedPrice),
                        reason = command.ChangeReason,
                        status = "Pending",
                        requestedByUserId = userId,
                        requestedAt = now
                    },
                    transaction);

                var dto = await LoadPriceChangeRequestDtoAsync(connection, transaction, workspaceId, requestId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "PriceChangeRequestCreated", EntityType.PriceChangeRequest, requestId, null, afterJson, command.ChangeReason, command.ClientRequestId, collector);

                return (dto, EntityType.PriceChangeRequest, requestId);
            },
            cancellationToken);
    }
}
