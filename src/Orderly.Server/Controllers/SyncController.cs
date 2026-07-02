using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Sync;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/sync")]
public class SyncController : CloudControllerBase
{
    public SyncController(ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
    }

    [HttpPost("snapshots")]
    public Task<ActionResult<SnapshotTokenResponse>> CreateSnapshotAsync(Guid workspaceId, [FromBody] SnapshotRequest request)
        => Task.FromResult<ActionResult<SnapshotTokenResponse>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpGet("snapshots/{snapshotToken}")]
    public Task<IActionResult> GetSnapshotPageAsync(Guid workspaceId, string snapshotToken, [FromQuery] string entityType, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpGet("changes")]
    public Task<ActionResult<ChangesResponse>> GetChangesAsync(Guid workspaceId, [FromQuery] long afterSequence, [FromQuery] int limit = 500)
        => Task.FromResult<ActionResult<ChangesResponse>>(StatusCode(501, new { Error = "Not implemented in this stage." }));
}
