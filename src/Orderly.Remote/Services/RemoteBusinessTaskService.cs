using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteBusinessTaskService : IBusinessTaskService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;
    private readonly IEmergencyDraftQueue? _offlineDraftQueue;

    public RemoteBusinessTaskService(RemoteCommerceClient client, CloudAuthSession session, IEmergencyDraftQueue? offlineDraftQueue = null)
    {
        _client = client;
        _session = session;
        _offlineDraftQueue = offlineDraftQueue;
    }

    public Task<BusinessTask> CreateAsync(BusinessTask task, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("当前离线或云端模式下暂不支持创建业务任务，请在本地开发模式使用。");

    public Task<BusinessTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var task = _client.GetAsync<CloudBusinessTaskDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/business-tasks/{id:N}",
            cancellationToken);
        return task.ContinueWith(t => t.Result?.ToEntity(), TaskScheduler.Default);
    }

    public async Task<IReadOnlyList<BusinessTask>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var paged = await _client.GetAsync<PagedList<CloudBusinessTaskDto>>(
            $"api/workspaces/{_session.WorkspaceId:N}/business-tasks?pageSize=200",
            cancellationToken).ConfigureAwait(false);

        return paged?.Items.Select(dto => dto.ToEntity()).ToList()
            ?? (IReadOnlyList<BusinessTask>)Array.Empty<BusinessTask>();
    }

    public Task UpdateAsync(BusinessTask task, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("当前离线或云端模式下暂不支持更新业务任务，请在本地开发模式使用。");

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("当前离线或云端模式下暂不支持删除业务任务，请在本地开发模式使用。");

    public async Task<BusinessTask> ChangeStatusAsync(
        Guid taskId,
        Orderly.Core.Commerce.TaskStatus newStatus,
        DateTime? completedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var latest = await _client.GetAsync<CloudBusinessTaskDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/business-tasks/{taskId:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new UpdateBusinessTaskStatusCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            BusinessTaskId = taskId,
            NewStatus = newStatus,
            CompletedAtUtc = completedAtUtc
        };

        try
        {
            var dto = await _client.PostAsync<UpdateBusinessTaskStatusCommand, CloudBusinessTaskDto>(
                $"api/workspaces/{_session.WorkspaceId:N}/business-tasks/{taskId:N}/status",
                command,
                cancellationToken).ConfigureAwait(false);

            return dto?.ToEntity()
                ?? throw new InvalidOperationException("任务状态更新后未返回数据。");
        }
        catch (HttpRequestException) when (_offlineDraftQueue is not null)
        {
            await _offlineDraftQueue.AddAsync(new EmergencyDraftDto
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityType = EntityType.BusinessTask,
                EntityId = taskId.ToString("N"),
                OperationType = "status",
                PayloadJson = JsonSerializer.Serialize(command),
                BaseRevision = command.ExpectedRevision,
                CreatedAtUtc = DateTime.UtcNow,
                Status = "Pending"
            }, cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException("当前离线，任务状态已保存为应急草稿，联网后自动提交。");
        }
    }
}
