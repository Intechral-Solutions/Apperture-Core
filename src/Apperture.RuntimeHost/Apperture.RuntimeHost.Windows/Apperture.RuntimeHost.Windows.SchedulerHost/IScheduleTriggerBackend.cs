namespace Apperture.RuntimeHost.Windows.SchedulerHost;

public interface IScheduleTriggerBackend
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
