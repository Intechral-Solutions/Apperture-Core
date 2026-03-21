using Microsoft.Extensions.Hosting;

namespace Apperture.RuntimeHost.Windows.SchedulerHost;

/// <summary>
/// Placeholder scheduler host. <strong>No real scheduler execution path exists.</strong>
/// ExecuteAsync returns immediately; no scheduled work is ever dispatched.
/// Runtime composition exposes "SchedulerDispatch" as NotYetImplementedExecutionSurfaces.
/// When a real scheduler is implemented, it must call ISchedulerLifecycleGuard before dispatch.
/// See Epic 27 HF — Epic27-Real-Deferred-Execution-Path-Inventory.md.
/// </summary>
public sealed class SchedulerHostStub : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
