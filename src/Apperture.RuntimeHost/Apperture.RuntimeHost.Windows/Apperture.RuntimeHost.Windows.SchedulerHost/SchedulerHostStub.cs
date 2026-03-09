using Microsoft.Extensions.Hosting;

namespace Apperture.RuntimeHost.Windows.SchedulerHost;

public sealed class SchedulerHostStub : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
