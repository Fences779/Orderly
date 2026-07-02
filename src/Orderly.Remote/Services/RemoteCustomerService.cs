using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteCustomerService : ICustomerService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemoteCustomerService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
    }

    public async Task<IReadOnlyList<CustomerRfmMetrics>> GetAllMetricsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        var paged = await _client.GetAsync<PagedList<CloudCustomerDto>>($"api/workspaces/{_session.WorkspaceId:N}/customers?pageSize=200", cancellationToken);
        if (paged == null) return Array.Empty<CustomerRfmMetrics>();
        return paged.Items.Select(ToMetrics).ToList();
    }

    public Task<CustomerRfmMetrics> GetMetricsAsync(Guid customerId, DateTime asOfUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(new CustomerRfmMetrics { CustomerId = customerId, Frequency = 0, Monetary = CommerceMoney.Zero });

    public Task<IReadOnlyList<RepurchaseReminder>> GetRepurchaseRemindersAsync(DateTime asOfUtc, int reminderThresholdDays = ICustomerService.DefaultReminderThresholdDays, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RepurchaseReminder>>(Array.Empty<RepurchaseReminder>());

    private static CustomerRfmMetrics ToMetrics(CloudCustomerDto dto)
    {
        int? recency = dto.LastOrderAtUtc.HasValue ? (int)(DateTime.UtcNow - dto.LastOrderAtUtc.Value).TotalDays : null;
        return new CustomerRfmMetrics
        {
            CustomerId = dto.Id,
            RecencyDays = recency,
            LastCompletedOrderAt = dto.LastOrderAtUtc,
            Frequency = dto.CompletedOrderCount,
            Monetary = CommerceMoney.From(dto.TotalSpend)
        };
    }
}
