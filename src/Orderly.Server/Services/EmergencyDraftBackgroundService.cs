namespace Orderly.Server.Services;

public sealed class EmergencyDraftBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _interval;

    public EmergencyDraftBackgroundService(IServiceProvider serviceProvider, TimeSpan? interval = null)
    {
        _serviceProvider = serviceProvider;
        _interval = interval ?? TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            await ProcessAllWorkspacesAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessAllWorkspacesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEmergencyDraftRepository>();
            var processor = scope.ServiceProvider.GetRequiredService<IEmergencyDraftProcessor>();

            var workspaceIds = await repository.ListWorkspaceIdsWithPendingAsync(cancellationToken);
            foreach (var workspaceId in workspaceIds)
            {
                try
                {
                    await processor.ProcessWorkspaceAsync(workspaceId, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to process emergency drafts for workspace {workspaceId}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Emergency draft background service failed: {ex.Message}");
        }
    }
}
