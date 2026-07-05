using System.Data;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Server.Mapping;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task<CommandResult<CloudBusinessTaskDto>> ChangeBusinessTaskStatusAsync(
        Guid workspaceId,
        Guid taskId,
        BusinessTaskStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        return await ExecuteWithIdempotencyAsync<BusinessTaskStatusCommand, CloudBusinessTaskDto>(
            workspaceId,
            "businessTask:status",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceBusinessTasks", taskId, command.ExpectedRevision, ct);

                var before = await LoadBusinessTaskDtoAsync(connection, transaction, workspaceId, taskId, ct);
                var beforeJson = await SnapshotJsonAsync(before);

                var completedAt = command.NewStatus == Orderly.Core.Commerce.TaskStatus.Completed
                    ? (command.CompletedAtUtc ?? DateTime.UtcNow)
                    : (DateTime?)null;

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceBusinessTasks""
                     SET ""Status"" = @status,
                         ""CompletedAt"" = @completedAt,
                         ""Revision"" = ""Revision"" + 1,
                         ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""LastChangeSequence"" = @sequence
                     WHERE ""Id"" = @taskId;",
                    new
                    {
                        status = (int)command.NewStatus,
                        completedAt,
                        now = DateTime.UtcNow,
                        updatedBy = userId,
                        sequence,
                        taskId
                    },
                    transaction);

                var dto = await LoadBusinessTaskDtoAsync(connection, transaction, workspaceId, taskId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "BusinessTaskStatusChanged", EntityType.BusinessTask, taskId, beforeJson, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.BusinessTask, taskId, "statusChanged", dto.Revision);

                return (dto, EntityType.BusinessTask, taskId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudBusinessTaskDto>> UpdateBusinessTaskStatusAsync(
        Guid workspaceId,
        Guid taskId,
        UpdateBusinessTaskStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        return await ChangeBusinessTaskStatusAsync(
            workspaceId,
            taskId,
            new BusinessTaskStatusCommand
            {
                ClientRequestId = command.ClientRequestId,
                ExpectedRevision = command.ExpectedRevision,
                TaskId = taskId,
                NewStatus = command.NewStatus,
                CompletedAtUtc = command.CompletedAtUtc
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CloudBusinessTaskDto> LoadBusinessTaskDtoAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid taskId, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceBusinessTasks\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @taskId;",
            new { workspaceId, taskId },
            transaction)
            ?? throw new InvalidOperationException($"任务 {taskId} 不存在。");

        return CommerceDtoMapper.ToBusinessTaskDto(row);
    }
}
