using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Models;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/exports")]
[ApiController]
public class ExportController : CloudControllerBase
{
    private readonly IExportService _exportService;

    public ExportController(
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions,
        IExportService exportService)
        : base(currentUser, authService, permissions)
    {
        _exportService = exportService;
    }

    [HttpPost("business-package")]
    public async Task<ActionResult<CloudExportJobDto>> CreateExportAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId))
        {
            return Forbid();
        }

        var membership = await GetMembershipAsync();
        if (!Permissions.CanExport(membership))
        {
            return Forbid();
        }

        var job = await _exportService.CreateJobAsync(workspaceId, UserId, "business-package", cancellationToken);
        return Accepted(job);
    }

    [HttpGet("{exportId:guid}")]
    public async Task<ActionResult<CloudExportJobDto>> GetExportAsync(Guid workspaceId, Guid exportId, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId))
        {
            return Forbid();
        }

        var membership = await GetMembershipAsync();
        if (!Permissions.CanExport(membership))
        {
            return Forbid();
        }

        var job = await _exportService.GetJobAsync(workspaceId, exportId, cancellationToken);
        if (job == null)
        {
            return NotFound();
        }

        return Ok(job);
    }

    [HttpGet("{exportId:guid}/download")]
    public async Task<IActionResult> DownloadExportAsync(Guid workspaceId, Guid exportId, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId))
        {
            return Forbid();
        }

        var membership = await GetMembershipAsync();
        if (!Permissions.CanExport(membership))
        {
            return Forbid();
        }

        var job = await _exportService.GetJobAsync(workspaceId, exportId, cancellationToken);
        if (job == null
            || job.Status != Orderly.Contracts.Offline.EmergencyDraftStatus.Submitted
            || string.IsNullOrWhiteSpace(job.FileName)
            || string.IsNullOrWhiteSpace(job.FilePath))
        {
            return NotFound();
        }

        var blobStorage = HttpContext.RequestServices.GetRequiredService<IBlobStorage>();
        if (blobStorage.IsEnabled)
        {
            var stream = await blobStorage.DownloadAsync(job.FilePath, cancellationToken);
            if (stream == null)
            {
                return NotFound();
            }

            return File(stream, "application/zip", job.FileName);
        }

        // Local file fallback.
        var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "orderly-exports", workspaceId.ToString("N")));
        var localPath = Path.GetFullPath(job.FilePath);
        if (!localPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(localPath))
        {
            return NotFound();
        }

        var fileStream = System.IO.File.OpenRead(localPath);
        return File(fileStream, "application/zip", job.FileName);
    }
}
