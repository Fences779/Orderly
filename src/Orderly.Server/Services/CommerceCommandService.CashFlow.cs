using System.Data;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Server.Mapping;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task<CommandResult<CloudCashFlowEntryDto>> RecordCashFlowAsync(Guid workspaceId, CashFlowEntryCommand command, string kind, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.CanManageCashFlow(membership))
            throw new UnauthorizedAccessException("没有现金流管理权限。");

        var (direction, settlementStatus, settledAmount) = kind.ToLowerInvariant() switch
        {
            "income" => (CashFlowDirection.Income, CashFlowSettlementStatus.Settled, command.Amount),
            "expense" => (CashFlowDirection.Expense, CashFlowSettlementStatus.Settled, command.Amount),
            "receivable" => (CashFlowDirection.Income, ResolveInitialStatus(command.OccurredAtUtc, command.DueDateUtc), 0m),
            "payable" => (CashFlowDirection.Expense, ResolveInitialStatus(command.OccurredAtUtc, command.DueDateUtc), 0m),
            _ => throw new InvalidOperationException($"不支持的现金流类型: {kind}")
        };

        return await ExecuteWithIdempotencyAsync<CashFlowEntryCommand, CloudCashFlowEntryDto>(
            workspaceId,
            $"cashflow:{kind}",
            command,
            async (connection, transaction, sequence, ct) =>
            {
                var now = DateTime.UtcNow;
                var entryId = Guid.NewGuid();

                await connection.ExecuteAsync(
                    @"INSERT INTO ""CommerceCashFlowEntries"" (
                        ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                        ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                        ""Direction"", ""Amount"", ""SettledAmount"", ""SettlementStatus"", ""OccurredAt"", ""DueDate"",
                        ""CategoryName"", ""OrderId"", ""BusinessKey"")
                    VALUES (
                        @id, @workspaceId, @now, @now, NULL, 0,
                        NULL, 1, @createdBy, @updatedBy, @sequence,
                        @direction, @amount, @settledAmount, @settlementStatus, @occurredAt, @dueDate,
                        @categoryName, @orderId, @businessKey);",
                    new
                    {
                        id = entryId,
                        workspaceId,
                        now,
                        createdBy = userId,
                        updatedBy = userId,
                        sequence,
                        direction = (int)direction,
                        amount = RoundMoney(command.Amount),
                        settledAmount = RoundMoney(settledAmount),
                        settlementStatus = (int)settlementStatus,
                        occurredAt = command.OccurredAtUtc,
                        dueDate = command.DueDateUtc,
                        categoryName = command.CategoryName,
                        command.OrderId,
                        businessKey = command.BusinessKey
                    },
                    transaction);

                var dto = await LoadCashFlowDtoAsync(connection, transaction, workspaceId, entryId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, $"CashFlow{kind}Created", EntityType.CashFlowEntry, entryId, null, afterJson, command.Reason, command.ClientRequestId);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.CashFlowEntry, entryId, "created", dto.Revision);

                return (dto, EntityType.CashFlowEntry, entryId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudCashFlowEntryDto>> SettleCashFlowAsync(Guid workspaceId, Guid entryId, SettleCashFlowCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.CanManageCashFlow(membership))
            throw new UnauthorizedAccessException("没有现金流管理权限。");

        if (command.Amount <= 0m)
            throw new InvalidOperationException("结算金额必须大于 0。");

        return await ExecuteWithIdempotencyAsync<SettleCashFlowCommand, CloudCashFlowEntryDto>(
            workspaceId,
            "cashflow:settle",
            command,
            async (connection, transaction, sequence, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceCashFlowEntries", entryId, command.ExpectedRevision, ct);

                var before = await LoadCashFlowDtoAsync(connection, transaction, workspaceId, entryId, ct);
                var beforeJson = await SnapshotJsonAsync(before);

                var entryRow = await connection.QueryFirstOrDefaultAsync(
                    "SELECT * FROM \"CommerceCashFlowEntries\" WHERE \"Id\" = @entryId;",
                    new { entryId },
                    transaction)
                    ?? throw new InvalidOperationException($"现金流条目 {entryId} 不存在。");

                var amount = (decimal)entryRow.Amount;
                var currentSettled = (decimal)entryRow.SettledAmount;
                var newSettled = Math.Min(currentSettled + command.Amount, amount);
                var dueDate = entryRow.DueDate as DateTime?;
                var newStatus = ResolveSettlementStatus(newSettled, amount, dueDate, command.AsOfUtc);

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceCashFlowEntries""
                     SET ""SettledAmount"" = @newSettled,
                         ""SettlementStatus"" = @newStatus,
                         ""Revision"" = ""Revision"" + 1,
                         ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""LastChangeSequence"" = @sequence
                     WHERE ""Id"" = @entryId;",
                    new
                    {
                        newSettled = RoundMoney(newSettled),
                        newStatus = (int)newStatus,
                        now = DateTime.UtcNow,
                        updatedBy = userId,
                        sequence,
                        entryId
                    },
                    transaction);

                var dto = await LoadCashFlowDtoAsync(connection, transaction, workspaceId, entryId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "CashFlowSettled", EntityType.CashFlowEntry, entryId, beforeJson, afterJson, command.Reason, command.ClientRequestId);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.CashFlowEntry, entryId, "settled", dto.Revision);

                return (dto, EntityType.CashFlowEntry, entryId);
            },
            cancellationToken);
    }

    private static CashFlowSettlementStatus ResolveInitialStatus(DateTime occurredAt, DateTime? dueDate)
        => dueDate.HasValue && dueDate.Value < occurredAt ? CashFlowSettlementStatus.Overdue : CashFlowSettlementStatus.Pending;

    private static CashFlowSettlementStatus ResolveSettlementStatus(decimal settled, decimal gross, DateTime? dueDate, DateTime asOfUtc)
    {
        if (settled >= gross)
            return CashFlowSettlementStatus.Settled;

        if (dueDate is DateTime due && asOfUtc > due)
            return CashFlowSettlementStatus.Overdue;

        return settled > 0m ? CashFlowSettlementStatus.PartiallySettled : CashFlowSettlementStatus.Pending;
    }

    private async Task<CloudCashFlowEntryDto> LoadCashFlowDtoAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid entryId, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceCashFlowEntries\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @entryId;",
            new { workspaceId, entryId },
            transaction)
            ?? throw new InvalidOperationException($"现金流条目 {entryId} 不存在。");

        return CommerceDtoMapper.ToCashFlowEntryDto(row);
    }
}
