using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Sync;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/sync")]
[ApiController]
public class SyncController : CloudControllerBase
{
    private readonly IWorkspaceSyncQueryService _syncQueryService;

    public SyncController(
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions,
        IWorkspaceSyncQueryService syncQueryService)
        : base(currentUser, authService, permissions)
    {
        _syncQueryService = syncQueryService;
    }

    [HttpPost("snapshots")]
    public async Task<ActionResult<SnapshotTokenResponse>> CreateSnapshotAsync(Guid workspaceId, [FromBody] SnapshotRequest request, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId))
        {
            return Forbid();
        }

        var response = await _syncQueryService.CreateSnapshotAsync(workspaceId, request.EntityType, cancellationToken);
        return Ok(response);
    }

    [HttpGet("snapshots/{snapshotToken}")]
    public async Task<ActionResult<SnapshotPageResponse<object>>> GetSnapshotPageAsync(
        Guid workspaceId,
        string snapshotToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId))
        {
            return Forbid();
        }

        var response = await _syncQueryService.GetSnapshotPageAsync(snapshotToken, page, pageSize, cancellationToken);
        return Ok(response);
    }

    [HttpGet("changes")]
    public async Task<ActionResult<ChangesResponse>> GetChangesAsync(
        Guid workspaceId,
        [FromQuery] long afterSequence = 0,
        [FromQuery] int maxCount = 200,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId))
        {
            return Forbid();
        }

        var response = await _syncQueryService.GetChangesAsync(workspaceId, afterSequence, maxCount, cancellationToken);
        return Ok(response);
    }
}
