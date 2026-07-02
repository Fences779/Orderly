using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/price-change-requests")]
public class PriceChangeController : CloudControllerBase
{
    private readonly CommerceCommandService _commandService;

    public PriceChangeController(CommerceCommandService commandService, ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _commandService = commandService;
    }

    [HttpGet]
    public Task<ActionResult<PagedList<CloudPriceChangeRequestDto>>> ListAsync(Guid workspaceId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Task.FromResult<ActionResult<PagedList<CloudPriceChangeRequestDto>>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost]
    public async Task<ActionResult<CloudPriceChangeRequestDto>> CreateAsync(Guid workspaceId, [FromBody] PriceChangeRequestCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.CreatePriceChangeRequestAsync(workspaceId, command);
        return Ok(result.Value);
    }

    [HttpPost("{requestId:guid}/approve")]
    public async Task<ActionResult<CloudPriceChangeRequestDto>> ApproveAsync(Guid workspaceId, Guid requestId, [FromBody] ReviewPriceChangeCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.ApprovePriceChangeRequestAsync(workspaceId, requestId, command);
        return Ok(result.Value);
    }

    [HttpPost("{requestId:guid}/reject")]
    public async Task<ActionResult<CloudPriceChangeRequestDto>> RejectAsync(Guid workspaceId, Guid requestId, [FromBody] ReviewPriceChangeCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.RejectPriceChangeRequestAsync(workspaceId, requestId, command);
        return Ok(result.Value);
    }
}
