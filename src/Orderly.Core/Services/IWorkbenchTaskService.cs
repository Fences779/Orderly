using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IWorkbenchTaskService
{
    Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(CancellationToken cancellationToken = default);
}
