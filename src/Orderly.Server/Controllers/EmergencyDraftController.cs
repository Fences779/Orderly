using Dapper;
using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Offline;
using Orderly.Server.Models;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/emergency-drafts")]
[ApiController]
public class EmergencyDraftController : CloudControllerBase
{
    private readonly IEmergencyDraftRepository _repository;

    public EmergencyDraftController(
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions,
        IEmergencyDraftRepository repository)
        : base(currentUser, authService, permissions)
    {
        _repository = repository;
    }

    [HttpPost]
    public async Task<IActionResult> SubmitAsync(Guid workspaceId, [FromBody] EmergencyDraftDto draft, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId))
        {
            return Forbid();
        }

        var isAllowed = EmergencyDraftAllowedOperations.IsAllowed(draft.EntityType, draft.OperationType);
        if (isAllowed && string.IsNullOrWhiteSpace(draft.EntityId))
        {
            return BadRequest(new { Error = "应急草稿缺少目标实体 Id。" });
        }

        Guid? entityId = null;
        if (!string.IsNullOrWhiteSpace(draft.EntityId))
        {
            if (!Guid.TryParse(draft.EntityId, out var parsedEntityId))
            {
                return BadRequest(new { Error = "应急草稿目标实体 Id 格式不正确。" });
            }

            entityId = parsedEntityId;
        }

        var status = isAllowed ? EmergencyDraftStatus.Pending : EmergencyDraftStatus.Rejected;

        if (!Guid.TryParse(draft.Id, out var draftId))
        {
            return BadRequest(new { Error = "应急草稿 Id 是必填的幂等键。" });
        }

        var existing = await _repository.GetAsync(draftId, cancellationToken);
        if (existing != null)
        {
            if (existing.WorkspaceId != workspaceId)
            {
                return Forbid();
            }

            return Accepted(new { DraftId = existing.Id, Status = existing.Status });
        }

        var record = new CloudEmergencyDraftRecord
        {
            Id = draftId,
            WorkspaceId = workspaceId,
            SubmittedByUserId = UserId,
            EntityType = draft.EntityType,
            EntityId = entityId,
            OperationType = draft.OperationType,
            PayloadJson = draft.PayloadJson,
            BaseRevision = draft.BaseRevision,
            Status = status,
            LastSubmitError = isAllowed ? null : "该操作类型不允许作为应急草稿提交。",
            CreatedAt = draft.CreatedAtUtc == default ? DateTime.UtcNow : draft.CreatedAtUtc,
            SubmittedAt = null
        };

        await _repository.AddAsync(record, cancellationToken);

        if (!isAllowed)
        {
            return BadRequest(new { Error = record.LastSubmitError });
        }

        return Accepted(new { DraftId = record.Id, Status = record.Status });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CloudEmergencyDraftRecord>>> ListAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId))
        {
            return Forbid();
        }

        var drafts = await _repository.ListByWorkspaceAsync(workspaceId, cancellationToken);
        return Ok(drafts);
    }
}
