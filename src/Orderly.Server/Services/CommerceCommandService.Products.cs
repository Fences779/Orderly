using System.Data;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Server.Mapping;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task<CommandResult<CloudProductDto>> CreateProductAsync(Guid workspaceId, CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.IsAdmin(membership))
            throw new UnauthorizedAccessException("没有商品管理权限。");

        if (string.IsNullOrWhiteSpace(command.Name))
            throw new InvalidOperationException("商品名称不能为空。");

        if (string.IsNullOrWhiteSpace(command.Code))
            throw new InvalidOperationException("商品编码不能为空。");

        return await ExecuteWithIdempotencyAsync<CreateProductCommand, CloudProductDto>(
            workspaceId,
            "product:create",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                var now = DateTime.UtcNow;
                var productId = Guid.NewGuid();

                await connection.ExecuteAsync(
                    @"INSERT INTO ""CommerceProducts"" (
                        ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                        ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                        ""Name"", ""Code"", ""ProductType"", ""Description"", ""DefaultUnitId"", ""SupplierId"", ""DefaultPrice"", ""DefaultCost"")
                    VALUES (
                        @id, @workspaceId, @now, @now, NULL, 0,
                        NULL, 1, @createdBy, @updatedBy, @sequence,
                        @name, @code, @productType, @description, @defaultUnitId, @supplierId, @defaultPrice, @defaultCost);",
                    new
                    {
                        id = productId,
                        workspaceId,
                        now,
                        createdBy = userId,
                        updatedBy = userId,
                        sequence,
                        name = command.Name.Trim(),
                        code = command.Code.Trim(),
                        productType = (int)command.ProductType,
                        description = command.Description,
                        defaultUnitId = command.DefaultUnitId,
                        supplierId = command.SupplierId,
                        defaultPrice = RoundMoney(command.DefaultPrice),
                        defaultCost = command.DefaultCost.HasValue ? RoundMoney(command.DefaultCost.Value) : (decimal?)null
                    },
                    transaction);

                var dto = await LoadProductDtoAsync(connection, transaction, workspaceId, productId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "ProductCreated", EntityType.Product, productId, null, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Product, productId, "created", dto.Revision);

                return (dto, EntityType.Product, productId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudProductDto>> UpdateProductAsync(Guid workspaceId, Guid productId, UpdateProductCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.IsAdmin(membership))
            throw new UnauthorizedAccessException("没有商品管理权限。");

        return await ExecuteWithIdempotencyAsync<UpdateProductCommand, CloudProductDto>(
            workspaceId,
            "product:update",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceProducts", productId, command.ExpectedRevision, ct, command, EntityType.Product);

                var before = await LoadProductDtoAsync(connection, transaction, workspaceId, productId, ct);
                var beforeJson = await SnapshotJsonAsync(before);

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceProducts""
                     SET ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""Revision"" = ""Revision"" + 1,
                         ""LastChangeSequence"" = @sequence,
                         ""Name"" = COALESCE(NULLIF(@name, ''), ""Name""),
                         ""Code"" = COALESCE(NULLIF(@code, ''), ""Code""),
                         ""ProductType"" = COALESCE(@productType, ""ProductType""),
                         ""Description"" = COALESCE(@description, ""Description""),
                         ""DefaultUnitId"" = COALESCE(@defaultUnitId, ""DefaultUnitId""),
                         ""SupplierId"" = COALESCE(@supplierId, ""SupplierId""),
                         ""DefaultPrice"" = COALESCE(@defaultPrice, ""DefaultPrice""),
                         ""DefaultCost"" = COALESCE(@defaultCost, ""DefaultCost"")
                     WHERE ""Id"" = @productId;",
                    new
                    {
                        now = DateTime.UtcNow,
                        updatedBy = userId,
                        sequence,
                        productId,
                        name = command.Name,
                        code = command.Code,
                        productType = command.ProductType.HasValue ? (int?)command.ProductType.Value : null,
                        description = command.Description,
                        defaultUnitId = command.DefaultUnitId,
                        supplierId = command.SupplierId,
                        defaultPrice = command.DefaultPrice.HasValue ? RoundMoney(command.DefaultPrice.Value) : (decimal?)null,
                        defaultCost = command.DefaultCost.HasValue ? RoundMoney(command.DefaultCost.Value) : (decimal?)null
                    },
                    transaction);

                var dto = await LoadProductDtoAsync(connection, transaction, workspaceId, productId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "ProductUpdated", EntityType.Product, productId, beforeJson, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Product, productId, "updated", dto.Revision);

                return (dto, EntityType.Product, productId);
            },
            cancellationToken);
    }

    private async Task<CloudProductDto> LoadProductDtoAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid productId, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceProducts\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @productId;",
            new { workspaceId, productId },
            transaction)
            ?? throw new InvalidOperationException($"商品 {productId} 不存在。");

        return CommerceDtoMapper.ToProductDto(row, canViewCosts: true);
    }
}
