using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/import")]
[ApiController]
public class ImportController : CloudControllerBase
{
    private readonly ICloudImportService _importService;

    public ImportController(
        ICloudImportService importService,
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _importService = importService;
    }

    [HttpPost("dry-run")]
    public async Task<ActionResult<LocalImportDryRunResponse>> DryRunAsync(Guid workspaceId, [FromBody] LocalImportDryRunRequest request, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var response = await _importService.DryRunAsync(workspaceId, request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("commit")]
    public async Task<ActionResult<LocalImportCommitResponse>> CommitAsync(Guid workspaceId, [FromBody] LocalImportCommitRequest request, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var response = await _importService.CommitAsync(workspaceId, request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("batches/{batchId:guid}")]
    public async Task<ActionResult<LocalImportBatchStatusDto>> GetBatchStatusAsync(Guid workspaceId, Guid batchId, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var status = await _importService.GetBatchStatusAsync(workspaceId, batchId, cancellationToken);
        if (status == null) return NotFound();
        return Ok(status);
    }
}
