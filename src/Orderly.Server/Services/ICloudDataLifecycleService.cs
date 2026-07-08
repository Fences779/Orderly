using Microsoft.AspNetCore.Http;
using Orderly.Contracts.Commerce;

namespace Orderly.Server.Services;

public interface ICloudDataLifecycleService
{
    Task<IReadOnlyList<CloudEntityVersionDto>> ListHistoryAsync(Guid workspaceId, string entityType, Guid entityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudAttachmentDto>> ListAttachmentsAsync(Guid workspaceId, string entityType, Guid entityId, CancellationToken cancellationToken = default);
    Task<CloudAttachmentDto> UploadAttachmentAsync(Guid workspaceId, string entityType, Guid entityId, IFormFile file, string? clientRequestId, CancellationToken cancellationToken = default);
    Task<(CloudAttachmentDto Attachment, Stream Content)> DownloadAttachmentAsync(Guid workspaceId, Guid attachmentId, CancellationToken cancellationToken = default);
    Task<bool> ArchiveAttachmentAsync(Guid workspaceId, Guid attachmentId, string? reason, string? clientRequestId, CancellationToken cancellationToken = default);
    Task<bool> PermanentlyDeleteArchivedEntityAsync(Guid workspaceId, string entityType, Guid entityId, PermanentDeleteRequest request, CancellationToken cancellationToken = default);
}
