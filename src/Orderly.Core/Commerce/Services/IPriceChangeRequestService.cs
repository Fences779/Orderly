namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Submits and reviews product price-change requests in a cloud workspace.
/// This service is only meaningful when connected to Orderly.Server; local-only
/// deployments can provide a no-op or unsupported implementation.
/// </summary>
public interface IPriceChangeRequestService
{
    /// <summary>Submits a new price-change request for the supplied product.</summary>
    Task SubmitAsync(Guid productId, decimal proposedPrice, string? reason, CancellationToken cancellationToken = default);
}
