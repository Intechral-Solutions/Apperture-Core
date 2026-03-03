using Microsoft.Extensions.Hosting;

namespace Apperture.Scheduler.Host;

public sealed class SchedulerHostStub : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
