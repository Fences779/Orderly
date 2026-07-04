using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface IEmergencyDraftProcessor
{
    Task ProcessWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

public sealed class EmergencyDraftProcessor : IEmergencyDraftProcessor
{
    private readonly IEmergencyDraftRepository _repository;
    private readonly IServiceProvider _serviceProvider;

    public EmergencyDraftProcessor(IEmergencyDraftRepository repository, IServiceProvider serviceProvider)
    {
        _repository = repository;
        _serviceProvider = serviceProvider;
    }

    public async Task ProcessWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var pending = await _repository.ListPendingAsync(workspaceId, cancellationToken);

        foreach (var draft in pending)
        {
            try
            {
                await ProcessDraftAsync(draft, cancellationToken);
            }
            catch (Exception ex)
            {
                await _repository.UpdateStatusAsync(
                    draft.Id,
                    EmergencyDraftStatus.Failed,
                    ex.Message,
                    DateTime.UtcNow,
                    cancellationToken);
            }
        }
    }

    private async Task ProcessDraftAsync(CloudEmergencyDraftRecord draft, CancellationToken cancellationToken)
    {
        if (!draft.EntityId.HasValue)
        {
            await MarkFailedAsync(draft, "草稿缺少目标实体 Id。", cancellationToken);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<ICloudAuthService>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserContext>();

        var user = await authService.GetUserAsync(draft.SubmittedByUserId);
        var membership = await authService.GetMembershipAsync(draft.SubmittedByUserId);

        if (user == null || membership == null || !user.IsEnabled || !membership.IsEnabled)
        {
            await MarkFailedAsync(draft, "提交人账号或成员身份已失效，无法重放。", cancellationToken);
            return;
        }

        currentUser.Set(
            user.Id,
            user.Username,
            user.DisplayName,
            membership.CloudRole,
            membership.BusinessLabel,
            membership.WorkspaceId,
            user.TokenVersion);

        var commandService = scope.ServiceProvider.GetRequiredService<CommerceCommandService>();
        var key = $"{draft.EntityType}/{draft.OperationType}";

        if (string.Equals(key, EmergencyDraftAllowedOperations.CustomerNote, StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteCustomerNoteAsync(commandService, draft, cancellationToken);
            return;
        }

        if (string.Equals(key, EmergencyDraftAllowedOperations.OrderStage, StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteOrderStageAsync(commandService, draft, cancellationToken);
            return;
        }

        if (string.Equals(key, EmergencyDraftAllowedOperations.OrderNote, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, EmergencyDraftAllowedOperations.BusinessTaskStatus, StringComparison.OrdinalIgnoreCase))
        {
            await MarkFailedAsync(draft, $"{key} 类型的服务端重放尚未实现。", cancellationToken);
            return;
        }

        await MarkFailedAsync(draft, $"{key} 不是允许的应急草稿类型。", cancellationToken);
    }

    private async Task ExecuteCustomerNoteAsync(CommerceCommandService commandService, CloudEmergencyDraftRecord draft, CancellationToken cancellationToken)
    {
        var command = JsonSerializer.Deserialize<CustomerNoteCommand>(draft.PayloadJson)
            ?? throw new InvalidOperationException("无法反序列化客户备注命令。");

        command.ClientRequestId = draft.Id.ToString("N");
        command.ExpectedRevision = draft.BaseRevision ?? command.ExpectedRevision;

        await commandService.AddCustomerNoteAsync(
            draft.WorkspaceId,
            draft.EntityId!.Value,
            command,
            cancellationToken);

        await MarkSubmittedAsync(draft, cancellationToken);
    }

    private async Task ExecuteOrderStageAsync(CommerceCommandService commandService, CloudEmergencyDraftRecord draft, CancellationToken cancellationToken)
    {
        var command = JsonSerializer.Deserialize<OrderStageCommand>(draft.PayloadJson)
            ?? throw new InvalidOperationException("无法反序列化订单阶段命令。");

        command.ClientRequestId = draft.Id.ToString("N");
        command.ExpectedRevision = draft.BaseRevision ?? command.ExpectedRevision;

        var dimension = InferStageDimension(command);
        if (string.IsNullOrEmpty(dimension))
        {
            await MarkFailedAsync(draft, "订单阶段命令未指定任何目标阶段。", cancellationToken);
            return;
        }

        await commandService.UpdateOrderStageAsync(
            draft.WorkspaceId,
            draft.EntityId!.Value,
            command,
            dimension,
            cancellationToken);

        await MarkSubmittedAsync(draft, cancellationToken);
    }

    private static string? InferStageDimension(OrderStageCommand command)
    {
        if (command.TargetSalesStage.HasValue) return "sales";
        if (command.TargetPaymentStage.HasValue) return "payment";
        if (command.TargetFulfillmentStage.HasValue) return "fulfillment";
        return null;
    }

    private async Task MarkSubmittedAsync(CloudEmergencyDraftRecord draft, CancellationToken cancellationToken)
    {
        await _repository.UpdateStatusAsync(draft.Id, EmergencyDraftStatus.Submitted, null, DateTime.UtcNow, cancellationToken);
    }

    private async Task MarkFailedAsync(CloudEmergencyDraftRecord draft, string error, CancellationToken cancellationToken)
    {
        await _repository.UpdateStatusAsync(draft.Id, EmergencyDraftStatus.Failed, error, null, cancellationToken);
    }
}
