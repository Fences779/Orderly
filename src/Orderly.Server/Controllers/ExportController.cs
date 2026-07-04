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
        if (job == null || job.Status != Orderly.Contracts.Offline.EmergencyDraftStatus.Submitted)
        {
            return NotFound();
        }

        var blobStorage = HttpContext.RequestServices.GetRequiredService<IBlobStorage>();
        if (blobStorage.IsEnabled)
        {
            var key = $"{GetExportPrefix()}{workspaceId:N}/{job.FileName}";
            var stream = await blobStorage.DownloadAsync(key, cancellationToken);
            if (stream == null)
            {
                return NotFound();
            }

            return File(stream, "application/zip", job.FileName ?? "export.zip");
        }

        // Local file fallback.
        var localDir = Path.Combine(Path.GetTempPath(), "orderly-exports", workspaceId.ToString("N"));
        var localPath = Path.Combine(localDir, job.FileName ?? string.Empty);
        if (!System.IO.File.Exists(localPath))
        {
            return NotFound();
        }

        var fileStream = System.IO.File.OpenRead(localPath);
        return File(fileStream, "application/zip", job.FileName ?? "export.zip");
    }

    private string GetExportPrefix()
    {
        var options = HttpContext.RequestServices.GetRequiredService<ServerOptions>();
        var prefix = options.OssExportPrefix;
        if (!prefix.EndsWith('/')) prefix += "/";
        return prefix;
    }
}
