using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/editing")]
public class EditingController : CloudControllerBase
{
    public EditingController(ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
    }

    [HttpPost("begin")]
    public Task<IActionResult> BeginAsync(Guid workspaceId, [FromBody] EditingPresenceCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("end")]
    public Task<IActionResult> EndAsync(Guid workspaceId, [FromBody] EditingPresenceCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));
}
