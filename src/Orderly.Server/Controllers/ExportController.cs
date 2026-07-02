using Microsoft.AspNetCore.Mvc;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/exports")]
public class ExportController : CloudControllerBase
{
    public ExportController(ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
    }

    [HttpPost("business-package")]
    public Task<IActionResult> CreateExportAsync(Guid workspaceId)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpGet("{exportId:guid}")]
    public Task<IActionResult> GetExportAsync(Guid workspaceId, Guid exportId)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));
}
