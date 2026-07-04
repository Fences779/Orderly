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
    private readonly CommerceCommandService _commandService;

    public EmergencyDraftProcessor(IEmergencyDraftRepository repository, CommerceCommandService commandService)
    {
        _repository = repository;
        _commandService = commandService;
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
        var key = $"{draft.EntityType}/{draft.OperationType}";

        if (!draft.EntityId.HasValue)
        {
            await MarkFailedAsync(draft, "草稿缺少目标实体 Id。", cancellationToken);
            return;
        }

        if (string.Equals(key, EmergencyDraftAllowedOperations.CustomerNote, StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteCustomerNoteAsync(draft, cancellationToken);
            return;
        }

        if (string.Equals(key, EmergencyDraftAllowedOperations.OrderStage, StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteOrderStageAsync(draft, cancellationToken);
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

    private async Task ExecuteCustomerNoteAsync(CloudEmergencyDraftRecord draft, CancellationToken cancellationToken)
    {
        var command = JsonSerializer.Deserialize<CustomerNoteCommand>(draft.PayloadJson)
            ?? throw new InvalidOperationException("无法反序列化客户备注命令。");

        command.ClientRequestId = draft.Id.ToString("N");
        command.ExpectedRevision = draft.BaseRevision ?? command.ExpectedRevision;

        await _commandService.AddCustomerNoteAsync(
            draft.WorkspaceId,
            draft.EntityId!.Value,
            command,
            cancellationToken);

        await MarkSubmittedAsync(draft, cancellationToken);
    }

    private async Task ExecuteOrderStageAsync(CloudEmergencyDraftRecord draft, CancellationToken cancellationToken)
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

        await _commandService.UpdateOrderStageAsync(
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
