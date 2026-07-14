using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Evaluates due schedules and dispatches workflow starts with lifecycle gating. Epic 28 / SWM-1.
/// </summary>
public sealed class ScheduleDispatchBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ScheduleDispatchOptions> _options;
    private readonly ILogger<ScheduleDispatchBackgroundService> _log;

    public ScheduleDispatchBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ScheduleDispatchOptions> options,
        ILogger<ScheduleDispatchBackgroundService> log)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(15, _options.Value.PollIntervalSeconds));
        var planeValue = ScheduleDispatchOptionsDefaults.ResolveRuntimePlaneId(_options.Value);
        var planeId = new RuntimePlaneId(planeValue);

        string hostId;
        await using (var startupScope = _scopeFactory.CreateAsyncScope())
        {
            hostId = startupScope.ServiceProvider.GetRequiredService<ISchedulerHostIdentity>().HostId;
        }

        _log.LogInformation(
            "Schedule dispatch loop starting. Plane={Plane} HostId={HostId} PollIntervalSeconds={PollIntervalSeconds}",
            planeValue,
            hostId,
            interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                var repo = sp.GetRequiredService<IScheduleRepository>();
                var evaluator = sp.GetRequiredService<IScheduleEvaluator>();
                var claimOptions = sp.GetRequiredService<ISchedulerClaimOptions>();
                var runner = sp.GetRequiredService<IScheduleDispatchRunner>();

                var asOfUtc = DateTimeOffset.UtcNow;
                var staleCutoffUtc = asOfUtc.AddMinutes(-claimOptions.ClaimLeaseMinutes);
                var assignments = await repo.GetAssignmentsAsync(planeId, stoppingToken);
                var due = await evaluator.GetDueAsync(planeId, asOfUtc, stoppingToken);
                var stale = await repo.GetRecoverableStaleCandidatesAsync(planeId, staleCutoffUtc, stoppingToken);
                var candidateCount = due.Count + stale.Count(s =>
                    !due.Any(d => d.ScheduleId == s.ScheduleId && d.FireTimeUtc == s.FireTimeUtc));

                _log.LogInformation(
                    "Schedule dispatch cycle begin. Plane={Plane} HostId={HostId} Assignments={Assignments} Due={Due} Stale={Stale} Candidates={Candidates}",
                    planeValue,
                    hostId,
                    assignments.Count,
                    due.Count,
                    stale.Count,
                    candidateCount);

                await runner.RunCycleAsync(planeId, asOfUtc, stoppingToken);

                _log.LogInformation(
                    "Schedule dispatch cycle completed. Plane={Plane} HostId={HostId} Candidates={Candidates}",
                    planeValue,
                    hostId,
                    candidateCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Schedule dispatch cycle failed. Plane={Plane} HostId={HostId}", planeValue, hostId);
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
