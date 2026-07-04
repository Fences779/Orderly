namespace Orderly.Server.Services;

public sealed class ExportBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _interval;

    public ExportBackgroundService(IServiceProvider serviceProvider, TimeSpan? interval = null)
    {
        _serviceProvider = serviceProvider;
        _interval = interval ?? TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var exportService = scope.ServiceProvider.GetRequiredService<IExportService>();
            await exportService.ProcessPendingJobsAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
