using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Microsoft.Extensions.Options;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Evaluates due schedules and dispatches workflow starts with lifecycle gating. Epic 28.
/// </summary>
public sealed class ScheduleDispatchBackgroundService : BackgroundService
{
    private readonly IScheduleDispatchRunner _runner;
    private readonly IOptions<ScheduleDispatchOptions> _options;
    private readonly ILogger<ScheduleDispatchBackgroundService> _log;

    public ScheduleDispatchBackgroundService(
        IScheduleDispatchRunner runner,
        IOptions<ScheduleDispatchOptions> options,
        ILogger<ScheduleDispatchBackgroundService> log)
    {
        _runner = runner;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(15, _options.Value.PollIntervalSeconds));
        var planeId = new RuntimePlaneId(_options.Value.RuntimePlaneId ?? "local");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _runner.RunCycleAsync(planeId, DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Schedule dispatch cycle failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
