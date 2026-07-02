using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/archive")]
public class ArchiveController : CloudControllerBase
{
    private readonly CommerceCommandService _commandService;

    public ArchiveController(CommerceCommandService commandService, ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _commandService = commandService;
    }

    [HttpPost("{entityType}/{entityId:guid}")]
    public async Task<IActionResult> ArchiveAsync(Guid workspaceId, string entityType, Guid entityId, [FromBody] ArchiveCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        command.EntityType = entityType;
        command.EntityId = entityId;
        var result = await _commandService.ArchiveAsync(workspaceId, entityType, entityId, command);
        return Ok(result.Value);
    }

    [HttpPost("{entityType}/{entityId:guid}/recover")]
    public async Task<IActionResult> RecoverAsync(Guid workspaceId, string entityType, Guid entityId, [FromBody] WriteCommandBase command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.RecoverAsync(workspaceId, entityType, entityId, command);
        return Ok(result.Value);
    }
}
