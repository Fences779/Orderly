using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/price-change-requests")]
public class PriceChangeController : CloudControllerBase
{
    public PriceChangeController(ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
    }

    [HttpGet]
    public Task<ActionResult<PagedList<CloudPriceChangeRequestDto>>> ListAsync(Guid workspaceId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Task.FromResult<ActionResult<PagedList<CloudPriceChangeRequestDto>>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost]
    public Task<ActionResult<CloudPriceChangeRequestDto>> CreateAsync(Guid workspaceId, [FromBody] PriceChangeRequestCommand command)
        => Task.FromResult<ActionResult<CloudPriceChangeRequestDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("{requestId:guid}/approve")]
    public Task<ActionResult<CloudPriceChangeRequestDto>> ApproveAsync(Guid workspaceId, Guid requestId, [FromBody] ReviewPriceChangeCommand command)
        => Task.FromResult<ActionResult<CloudPriceChangeRequestDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("{requestId:guid}/reject")]
    public Task<ActionResult<CloudPriceChangeRequestDto>> RejectAsync(Guid workspaceId, Guid requestId, [FromBody] ReviewPriceChangeCommand command)
        => Task.FromResult<ActionResult<CloudPriceChangeRequestDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));
}
