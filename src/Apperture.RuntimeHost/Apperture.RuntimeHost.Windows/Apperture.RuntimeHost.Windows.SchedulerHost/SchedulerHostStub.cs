using Microsoft.Extensions.Hosting;

namespace Apperture.RuntimeHost.Windows.SchedulerHost;

/// <summary>
/// Standalone scheduler host stub. <strong>Real scheduler runs in WorkerHost</strong> (Epic 28).
/// This process is a no-op placeholder; scheduled dispatch is implemented in
/// ScheduleDispatchBackgroundService within WorkerHost. See Epic28-Real-Scheduler-Implementation.md.
/// </summary>
public sealed class SchedulerHostStub : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
