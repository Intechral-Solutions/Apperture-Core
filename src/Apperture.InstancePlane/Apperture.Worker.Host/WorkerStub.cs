using Microsoft.Extensions.Hosting;

namespace Apperture.Worker.Host;

public sealed class WorkerStub : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
