using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/lifecycle")]
public sealed class LifecycleController : CloudControllerBase
{
    private readonly ICloudDataLifecycleService _lifecycleService;

    public LifecycleController(
        ICloudDataLifecycleService lifecycleService,
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _lifecycleService = lifecycleService;
    }

    [HttpGet("history/{entityType}/{entityId:guid}")]
    public async Task<ActionResult<IReadOnlyList<CloudEntityVersionDto>>> ListHistoryAsync(Guid workspaceId, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var history = await _lifecycleService.ListHistoryAsync(workspaceId, entityType, entityId, cancellationToken);
        return Ok(history);
    }

    [HttpGet("attachments/{entityType}/{entityId:guid}")]
    public async Task<ActionResult<IReadOnlyList<CloudAttachmentDto>>> ListAttachmentsAsync(Guid workspaceId, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var attachments = await _lifecycleService.ListAttachmentsAsync(workspaceId, entityType, entityId, cancellationToken);
        return Ok(attachments);
    }

    [HttpPost("attachments/{entityType}/{entityId:guid}")]
    [RequestSizeLimit(100_000_000)]
    public async Task<ActionResult<CloudAttachmentDto>> UploadAttachmentAsync(
        Guid workspaceId,
        string entityType,
        Guid entityId,
        [FromForm] IFormFile file,
        [FromForm] string? clientRequestId,
        CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        if (file == null) return BadRequest(new { Error = "Attachment file is required." });

        try
        {
            var attachment = await _lifecycleService.UploadAttachmentAsync(workspaceId, entityType, entityId, file, clientRequestId, cancellationToken);
            return Ok(attachment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpGet("attachments/{attachmentId:guid}/download")]
    public async Task<IActionResult> DownloadAttachmentAsync(Guid workspaceId, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();

        try
        {
            var (attachment, content) = await _lifecycleService.DownloadAttachmentAsync(workspaceId, attachmentId, cancellationToken);
            return File(content, attachment.ContentType, attachment.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { Error = ex.Message });
        }
    }

    [HttpPost("attachments/{attachmentId:guid}/archive")]
    public async Task<IActionResult> ArchiveAttachmentAsync(Guid workspaceId, Guid attachmentId, [FromBody] ArchiveCommand command, CancellationToken cancellationToken)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var ok = await _lifecycleService.ArchiveAttachmentAsync(workspaceId, attachmentId, command.ArchiveReason, command.ClientRequestId, cancellationToken);
        if (!ok) return NotFound(new { Error = "Attachment cannot be archived." });
        return NoContent();
    }

    [HttpDelete("permanent/{entityType}/{entityId:guid}")]
    public async Task<IActionResult> PermanentlyDeleteAsync(Guid workspaceId, string entityType, Guid entityId, [FromBody] PermanentDeleteRequest request, CancellationToken cancellationToken)
    {
        var membership = await GetMembershipAsync();
        if (membership.WorkspaceId != workspaceId) return Forbid();
        if (!Permissions.CanManageUsers(membership)) return Forbid();

        try
        {
            var ok = await _lifecycleService.PermanentlyDeleteArchivedEntityAsync(workspaceId, entityType, entityId, request, cancellationToken);
            if (!ok) return BadRequest(new { Error = "Permanent delete requires confirmation, archived state, and retention period." });
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}
