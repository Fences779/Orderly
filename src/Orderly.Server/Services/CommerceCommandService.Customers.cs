using System.Data;
using System.Text.Json;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Server.Mapping;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task<CommandResult<CloudCustomerDto>> CreateCustomerAsync(Guid workspaceId, CreateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.CanWriteBusinessData(membership))
            throw new UnauthorizedAccessException("没有客户写入权限。");

        if (string.IsNullOrWhiteSpace(command.Name))
            throw new InvalidOperationException("客户名称不能为空。");

        return await ExecuteWithIdempotencyAsync<CreateCustomerCommand, CloudCustomerDto>(
            workspaceId,
            "customer:create",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                var now = DateTime.UtcNow;
                var customerId = Guid.NewGuid();

                await connection.ExecuteAsync(
                    @"INSERT INTO ""CommerceCustomers"" (
                        ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                        ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                        ""Name"", ""Phone"", ""WeChat"", ""Email"", ""AssignedToUserId"")
                    VALUES (
                        @id, @workspaceId, @now, @now, NULL, 0,
                        NULL, 1, @createdBy, @updatedBy, @sequence,
                        @name, @phone, @weChat, @email, @assignedToUserId);",
                    new
                    {
                        id = customerId,
                        workspaceId,
                        now,
                        createdBy = userId,
                        updatedBy = userId,
                        sequence,
                        name = command.Name.Trim(),
                        phone = command.Phone,
                        weChat = command.WeChat,
                        email = command.Email,
                        assignedToUserId = command.AssignedToUserId
                    },
                    transaction);

                var dto = await LoadCustomerDtoAsync(connection, transaction, workspaceId, customerId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "CustomerCreated", EntityType.Customer, customerId, null, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Customer, customerId, "created", dto.Revision);

                return (dto, EntityType.Customer, customerId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudCustomerDto>> UpdateCustomerAsync(Guid workspaceId, Guid customerId, UpdateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.CanWriteBusinessData(membership))
            throw new UnauthorizedAccessException("没有客户写入权限。");

        return await ExecuteWithIdempotencyAsync<UpdateCustomerCommand, CloudCustomerDto>(
            workspaceId,
            "customer:update",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceCustomers", customerId, command.ExpectedRevision, ct, command, EntityType.Customer);

                var before = await LoadCustomerDtoAsync(connection, transaction, workspaceId, customerId, ct);
                var beforeJson = await SnapshotJsonAsync(before);

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceCustomers""
                     SET ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""Revision"" = ""Revision"" + 1,
                         ""LastChangeSequence"" = @sequence,
                         ""Name"" = COALESCE(NULLIF(@name, ''), ""Name""),
                         ""Phone"" = COALESCE(@phone, ""Phone""),
                         ""WeChat"" = COALESCE(@weChat, ""WeChat""),
                         ""Email"" = COALESCE(@email, ""Email""),
                         ""AssignedToUserId"" = COALESCE(@assignedToUserId, ""AssignedToUserId"")
                     WHERE ""Id"" = @customerId;",
                    new
                    {
                        now = DateTime.UtcNow,
                        updatedBy = userId,
                        sequence,
                        customerId,
                        name = command.Name,
                        phone = command.Phone,
                        weChat = command.WeChat,
                        email = command.Email,
                        assignedToUserId = command.AssignedToUserId
                    },
                    transaction);

                var dto = await LoadCustomerDtoAsync(connection, transaction, workspaceId, customerId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "CustomerUpdated", EntityType.Customer, customerId, beforeJson, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Customer, customerId, "updated", dto.Revision);

                return (dto, EntityType.Customer, customerId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudCustomerDto>> AddCustomerNoteAsync(Guid workspaceId, Guid customerId, CustomerNoteCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.CanWriteBusinessData(membership))
            throw new UnauthorizedAccessException("没有客户写入权限。");

        if (string.IsNullOrWhiteSpace(command.Note))
            throw new InvalidOperationException("备注内容不能为空。");

        return await ExecuteWithIdempotencyAsync<CustomerNoteCommand, CloudCustomerDto>(
            workspaceId,
            "customer:note",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceCustomers", customerId, command.ExpectedRevision, ct, command, EntityType.Customer);

                var before = await LoadCustomerDtoAsync(connection, transaction, workspaceId, customerId, ct);
                var beforeJson = await SnapshotJsonAsync(before);

                var customFields = ParseCustomFields(before.CustomFieldsJson);
                var notes = customFields.TryGetProperty("notes", out var notesElement)
                    ? notesElement.EnumerateArray()
                        .Select(e => e.GetString())
                        .Where(s => s != null)
                        .Cast<string>()
                        .ToList()
                    : new List<string>();
                notes.Add(command.Note.Trim());

                var updatedCustomFields = new Dictionary<string, object>(customFields.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone()))
                {
                    ["notes"] = notes
                };
                var customFieldsJson = JsonSerializer.Serialize(updatedCustomFields, JsonOptions);

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceCustomers""
                     SET ""CustomFieldsJson"" = @customFieldsJson,
                         ""Revision"" = ""Revision"" + 1,
                         ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""LastChangeSequence"" = @sequence
                     WHERE ""Id"" = @customerId;",
                    new { customFieldsJson, now = DateTime.UtcNow, updatedBy = userId, sequence, customerId },
                    transaction);

                var dto = await LoadCustomerDtoAsync(connection, transaction, workspaceId, customerId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "CustomerNoteAdded", EntityType.Customer, customerId, beforeJson, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Customer, customerId, "noteAdded", dto.Revision);

                return (dto, EntityType.Customer, customerId);
            },
            cancellationToken);
    }

    private async Task<CloudCustomerDto> LoadCustomerDtoAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid customerId, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceCustomers\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @customerId;",
            new { workspaceId, customerId },
            transaction)
            ?? throw new InvalidOperationException($"客户 {customerId} 不存在。");

        return CommerceDtoMapper.ToCustomerDto(row);
    }

    private JsonElement ParseCustomFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return JsonDocument.Parse("{}").RootElement;

        try
        {
            return JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement;
        }
    }
}
