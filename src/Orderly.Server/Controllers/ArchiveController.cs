using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/archive")]
public class ArchiveController : CloudControllerBase
{
    public ArchiveController(ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
    }

    [HttpPost("{entityType}/{entityId:guid}")]
    public Task<IActionResult> ArchiveAsync(Guid workspaceId, string entityType, Guid entityId, [FromBody] ArchiveCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("{entityType}/{entityId:guid}/recover")]
    public Task<IActionResult> RecoverAsync(Guid workspaceId, string entityType, Guid entityId, [FromBody] WriteCommandBase command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));
}
