using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/editing")]
public class EditingController : CloudControllerBase
{
    private readonly CommerceCommandService _commandService;

    public EditingController(CommerceCommandService commandService, ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _commandService = commandService;
    }

    [HttpPost("begin")]
    public async Task<IActionResult> BeginAsync(Guid workspaceId, [FromBody] EditingPresenceCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        await _commandService.BeginEditingAsync(workspaceId, command, HttpContext.Connection.Id);
        return Ok();
    }

    [HttpPost("end")]
    public async Task<IActionResult> EndAsync(Guid workspaceId, [FromBody] EditingPresenceCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        await _commandService.EndEditingAsync(workspaceId, command, HttpContext.Connection.Id);
        return Ok();
    }
}
