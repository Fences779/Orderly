using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteCashFlowService : ICashFlowService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemoteCashFlowService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
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
        => throw new NotImplementedException("Remote cash-flow recording is not implemented in this stage.");

    public Task<CashFlowEntry> RecordExpenseAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Remote cash-flow recording is not implemented in this stage.");

    public Task<CashFlowEntry> RecordReceivableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Remote cash-flow recording is not implemented in this stage.");

    public Task<CashFlowEntry> RecordPayableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Remote cash-flow recording is not implemented in this stage.");

    public Task<CashFlowEntry> SettleAsync(Guid entryId, CommerceMoney amount, DateTime asOfUtc, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Remote cash-flow settle is not implemented in this stage.");

    private sealed class RemoteCashFlowSummaryDto
    {
        public decimal RealizedIncome { get; set; }
        public decimal RealizedExpense { get; set; }
        public decimal NetCashFlow { get; set; }
        public decimal OutstandingReceivable { get; set; }
        public decimal OutstandingPayable { get; set; }
    }
}
