using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteCustomerService : ICustomerService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;
    private readonly IEmergencyDraftQueue? _offlineDraftQueue;

    public RemoteCustomerService(RemoteCommerceClient client, CloudAuthSession session, IEmergencyDraftQueue? offlineDraftQueue = null)
    {
        _client = client;
        _session = session;
        _offlineDraftQueue = offlineDraftQueue;
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

    public async Task AddNoteAsync(Guid customerId, string note, long expectedRevision, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("备注内容不能为空。", nameof(note));

        var command = new CustomerNoteCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = expectedRevision,
            Note = note
        };

        try
        {
            await _client.PostAsync<CustomerNoteCommand, CloudCustomerDto>(
                $"api/workspaces/{_session.WorkspaceId:N}/customers/{customerId:N}/notes",
                command,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (_offlineDraftQueue is not null)
        {
            await _offlineDraftQueue.AddAsync(new EmergencyDraftDto
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityType = EntityType.Customer,
                EntityId = customerId.ToString("N"),
                OperationType = "note",
                PayloadJson = JsonSerializer.Serialize(command),
                BaseRevision = command.ExpectedRevision,
                CreatedAtUtc = DateTime.UtcNow,
                Status = "Pending"
            }, cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException("当前离线，客户备注已保存为应急草稿，联网后自动提交。");
        }
    }

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
