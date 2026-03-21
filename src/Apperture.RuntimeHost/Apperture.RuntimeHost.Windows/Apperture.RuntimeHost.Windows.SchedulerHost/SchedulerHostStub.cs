using Microsoft.Extensions.Hosting;

namespace Apperture.RuntimeHost.Windows.SchedulerHost;

/// <summary>
/// Placeholder scheduler host. When implemented, scheduled triggers must call <c>IWorkflowStarter.StartAsync</c> only;
/// lifecycle gating occurs at dispatch/definition resolution (Epic 27 — Epic27-Execution-Surface-Inventory.md).
/// </summary>
public sealed class SchedulerHostStub : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
