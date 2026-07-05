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
    private readonly ServerOptions _options;

    public ExportController(
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions,
        IExportService exportService,
        ServerOptions options)
        : base(currentUser, authService, permissions)
    {
        _exportService = exportService;
        _options = options;
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

        try
        {
            var job = await _exportService.CreateJobAsync(workspaceId, UserId, "business-package", cancellationToken);
            return Accepted(job);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("导出目录", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status507InsufficientStorage, new { Error = ex.Message });
        }
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

            await _exportService.RecordDownloadAsync(
                workspaceId,
                exportId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                cancellationToken);
            return File(stream, "application/zip", job.FileName);
        }

        var expectedRoot = Path.GetFullPath(Path.Combine(_options.LocalExportDirectory, workspaceId.ToString("N")));
        var localPath = Path.GetFullPath(job.FilePath);
        if (!IsUnderDirectory(localPath, expectedRoot))
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(localPath))
        {
            return NotFound();
        }

        var fileStream = System.IO.File.OpenRead(localPath);
        await _exportService.RecordDownloadAsync(
            workspaceId,
            exportId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        return File(fileStream, "application/zip", job.FileName);
    }

    private static bool IsUnderDirectory(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
