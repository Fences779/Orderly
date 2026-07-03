using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteCashFlowService : ICashFlowService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;
    private readonly IEmergencyDraftQueue? _offlineDraftQueue;

    public RemoteCashFlowService(RemoteCommerceClient client, CloudAuthSession session, IEmergencyDraftQueue? offlineDraftQueue = null)
    {
        _client = client;
        _session = session;
        _offlineDraftQueue = offlineDraftQueue;
    }

    public async Task<CashFlowPeriodSummary> GetPeriodSummaryAsync(DateRange period, CancellationToken cancellationToken = default)
    {
        var summary = await _client.GetAsync<object>($"api/workspaces/{_session.WorkspaceId:N}/cashflow/summary", cancellationToken);
        if (summary == null)
        {
            return new CashFlowPeriodSummary
            {
                Period = period,
                RealizedIncome = CommerceMoney.Zero,
                RealizedExpense = CommerceMoney.Zero,
                NetCashFlow = CommerceMoney.Zero,
                OutstandingReceivable = CommerceMoney.Zero,
                OutstandingPayable = CommerceMoney.Zero,
                HealthScore = 0
            };
        }

        var json = System.Text.Json.JsonSerializer.Serialize(summary);
        var dto = System.Text.Json.JsonSerializer.Deserialize<RemoteCashFlowSummaryDto>(json);
        return new CashFlowPeriodSummary
        {
            Period = period,
            RealizedIncome = CommerceMoney.From(dto?.RealizedIncome ?? 0m),
            RealizedExpense = CommerceMoney.From(dto?.RealizedExpense ?? 0m),
            NetCashFlow = CommerceMoney.From((dto?.RealizedIncome ?? 0m) - (dto?.RealizedExpense ?? 0m)),
            OutstandingReceivable = CommerceMoney.From(dto?.OutstandingReceivable ?? 0m),
            OutstandingPayable = CommerceMoney.From(dto?.OutstandingPayable ?? 0m),
            HealthScore = 50
        };
    }

    public Task<int> ComputeHealthScoreAsync(DateRange period, CancellationToken cancellationToken = default)
        => Task.FromResult(50);

    public Task<CashFlowEntry> RecordIncomeAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => RecordAsync(input, "income", CashFlowDirection.Income, cancellationToken);

    public Task<CashFlowEntry> RecordExpenseAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => RecordAsync(input, "expense", CashFlowDirection.Expense, cancellationToken);

    public Task<CashFlowEntry> RecordReceivableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => RecordAsync(input, "receivable", CashFlowDirection.Income, cancellationToken);

    public Task<CashFlowEntry> RecordPayableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => RecordAsync(input, "payable", CashFlowDirection.Expense, cancellationToken);

    public async Task<CashFlowEntry> SettleAsync(Guid entryId, CommerceMoney amount, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        if (amount.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Settlement amount cannot be negative.");
        }

        var latest = await _client.GetAsync<CloudCashFlowEntryDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/cashflow/entries/{entryId:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new SettleCashFlowCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            Amount = amount.Amount,
            AsOfUtc = asOfUtc
        };

        try
        {
            var dto = await _client.PostAsync<SettleCashFlowCommand, CloudCashFlowEntryDto>(
                $"api/workspaces/{_session.WorkspaceId:N}/cashflow/{entryId:N}/settle",
                command,
                cancellationToken).ConfigureAwait(false);

            if (dto is null)
            {
                throw new InvalidOperationException("Cash-flow settlement returned no data.");
            }

            return dto.ToEntity();
        }
        catch (HttpRequestException) when (_offlineDraftQueue is not null)
        {
            await _offlineDraftQueue.AddAsync(new EmergencyDraftDto
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityType = EntityType.CashFlowEntry,
                EntityId = entryId.ToString("N"),
                OperationType = "settle",
                PayloadJson = JsonSerializer.Serialize(command),
                BaseRevision = command.ExpectedRevision,
                CreatedAtUtc = DateTime.UtcNow,
                Status = "Pending"
            }, cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException("当前离线，现金结算已保存为应急草稿，联网后自动提交。");
        }
    }

    private async Task<CashFlowEntry> RecordAsync(CashFlowEntryInput input, string kind, CashFlowDirection direction, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var command = new CashFlowEntryCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = 0L,
            Direction = direction,
            Amount = input.Amount.Amount,
            OccurredAtUtc = input.OccurredAt,
            DueDateUtc = input.DueDate,
            CategoryName = input.CategoryName ?? string.Empty,
            OrderId = input.OrderId,
            BusinessKey = input.BusinessKey
        };

        try
        {
            var dto = await _client.PostAsync<CashFlowEntryCommand, CloudCashFlowEntryDto>(
                $"api/workspaces/{_session.WorkspaceId:N}/cashflow/{kind}",
                command,
                cancellationToken).ConfigureAwait(false);

            if (dto is null)
            {
                throw new InvalidOperationException($"Cash-flow {kind} recording returned no data.");
            }

            return dto.ToEntity();
        }
        catch (HttpRequestException) when (_offlineDraftQueue is not null)
        {
            await _offlineDraftQueue.AddAsync(new EmergencyDraftDto
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityType = EntityType.CashFlowEntry,
                EntityId = input.OrderId?.ToString("N"),
                OperationType = kind,
                PayloadJson = JsonSerializer.Serialize(command),
                BaseRevision = command.ExpectedRevision,
                CreatedAtUtc = DateTime.UtcNow,
                Status = "Pending"
            }, cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException("当前离线，现金流水已保存为应急草稿，联网后自动提交。");
        }
    }

    private sealed class RemoteCashFlowSummaryDto
    {
        public decimal RealizedIncome { get; set; }
        public decimal RealizedExpense { get; set; }
        public decimal NetCashFlow { get; set; }
        public decimal OutstandingReceivable { get; set; }
        public decimal OutstandingPayable { get; set; }
    }
}
