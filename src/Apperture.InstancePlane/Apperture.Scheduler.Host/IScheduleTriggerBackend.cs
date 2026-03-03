namespace Apperture.Scheduler.Host;

public interface IScheduleTriggerBackend
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
