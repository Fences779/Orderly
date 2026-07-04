using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/business-tasks")]
[ApiController]
public class BusinessTasksController : CloudControllerBase
{
    private readonly CommerceCommandService _commandService;

    public BusinessTasksController(
        CommerceCommandService commandService,
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _commandService = commandService;
    }

    [HttpPost("{taskId:guid}/status")]
    public async Task<ActionResult<CloudBusinessTaskDto>> ChangeStatusAsync(Guid workspaceId, Guid taskId, [FromBody] BusinessTaskStatusCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        command.TaskId = taskId;
        var result = await _commandService.ChangeBusinessTaskStatusAsync(workspaceId, taskId, command);
        return Ok(result.Value);
    }
}
