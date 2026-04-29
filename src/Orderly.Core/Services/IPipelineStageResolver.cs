using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IPipelineStageResolver
{
    Task<PipelineStageSnapshot> ResolveAsync(int customerId, int? orderId = null, CancellationToken cancellationToken = default);
}
