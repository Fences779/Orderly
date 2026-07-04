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
        var status = isAllowed ? EmergencyDraftStatus.Pending : EmergencyDraftStatus.Rejected;

        var record = new CloudEmergencyDraftRecord
        {
            Id = Guid.TryParse(draft.Id, out var parsedId) ? parsedId : Guid.NewGuid(),
            WorkspaceId = workspaceId,
            EntityType = draft.EntityType,
            EntityId = string.IsNullOrWhiteSpace(draft.EntityId) ? null : Guid.Parse(draft.EntityId),
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
