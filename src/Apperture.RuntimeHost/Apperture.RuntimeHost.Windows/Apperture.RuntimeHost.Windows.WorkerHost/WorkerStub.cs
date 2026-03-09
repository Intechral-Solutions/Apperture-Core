using Microsoft.Extensions.Hosting;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

public sealed class WorkerStub : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
