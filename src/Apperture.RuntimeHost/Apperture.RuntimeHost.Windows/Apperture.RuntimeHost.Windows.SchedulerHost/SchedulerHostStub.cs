using Microsoft.Extensions.Hosting;

namespace Apperture.RuntimeHost.Windows.SchedulerHost;

/// <summary>
/// Placeholder scheduler host. When implemented, scheduled triggers must:
/// 1. Call <c>ISchedulerLifecycleGuard.CanDispatchWorkflowAsync</c> before dispatch.
/// 2. If false, skip and record blocked (audit/log); do not pretend the schedule executed.
/// 3. If true, call <c>IWorkflowStarter.StartAsync</c> only — lifecycle gating also occurs at dispatch.
/// See Epic 27 corrective pass — Epic27-Corrective-Pass-Deferred-Execution-Chokepoints.md.
/// </summary>
public sealed class SchedulerHostStub : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
