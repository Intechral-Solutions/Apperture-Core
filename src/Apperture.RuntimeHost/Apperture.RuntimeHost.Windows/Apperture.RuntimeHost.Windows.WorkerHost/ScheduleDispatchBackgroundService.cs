using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Evaluates due schedules and dispatches workflow starts with lifecycle gating. Epic 28 / SWM-1 / SWM-6.
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
            var cycleStarted = DateTimeOffset.UtcNow;
            var assignmentCount = 0;
            var dueCount = 0;
            var staleCount = 0;
            var candidateCount = 0;
            var claimedCount = 0;
            var dispatchedCount = 0;
            var skippedCount = 0;
            string? errorSummary = null;
            DateTimeOffset? cycleCompleted = null;
            DateTimeOffset? successfulCompleted = null;

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                var repo = sp.GetRequiredService<IScheduleRepository>();
                var evaluator = sp.GetRequiredService<IScheduleEvaluator>();
                var claimOptions = sp.GetRequiredService<ISchedulerClaimOptions>();
                var runner = sp.GetRequiredService<IScheduleDispatchRunner>();
                var heartbeatStore = sp.GetRequiredService<ISchedulerDispatchHeartbeatStore>();

                var asOfUtc = DateTimeOffset.UtcNow;
                cycleStarted = asOfUtc;
                var staleCutoffUtc = asOfUtc.AddMinutes(-claimOptions.ClaimLeaseMinutes);
                var assignments = await repo.GetAssignmentsAsync(planeId, stoppingToken);
                var due = await evaluator.GetDueAsync(planeId, asOfUtc, stoppingToken);
                var stale = await repo.GetRecoverableStaleCandidatesAsync(planeId, staleCutoffUtc, stoppingToken);
                assignmentCount = assignments.Count;
                dueCount = due.Count;
                staleCount = stale.Count;
                candidateCount = due.Count + stale.Count(s =>
                    !due.Any(d => d.ScheduleId == s.ScheduleId && d.FireTimeUtc == s.FireTimeUtc));

                _log.LogInformation(
                    "Schedule dispatch cycle begin. Plane={Plane} HostId={HostId} Assignments={Assignments} Due={Due} Stale={Stale} Candidates={Candidates}",
                    planeValue,
                    hostId,
                    assignmentCount,
                    dueCount,
                    staleCount,
                    candidateCount);

                var result = await runner.RunCycleAsync(planeId, asOfUtc, stoppingToken);
                claimedCount = result.ClaimedCount;
                dispatchedCount = result.DispatchedCount;
                skippedCount = result.SkippedCount;
                cycleCompleted = DateTimeOffset.UtcNow;
                successfulCompleted = cycleCompleted;

                _log.LogInformation(
                    "Schedule dispatch cycle completed. Plane={Plane} HostId={HostId} Candidates={Candidates} Claimed={Claimed} Dispatched={Dispatched} Skipped={Skipped}",
                    planeValue,
                    hostId,
                    candidateCount,
                    claimedCount,
                    dispatchedCount,
                    skippedCount);

                await TryWriteHeartbeatAsync(
                    heartbeatStore,
                    planeValue,
                    hostId,
                    (int)interval.TotalSeconds,
                    cycleStarted,
                    cycleCompleted,
                    successfulCompleted,
                    errorSummary: null,
                    assignmentCount,
                    dueCount,
                    staleCount,
                    candidateCount,
                    claimedCount,
                    dispatchedCount,
                    skippedCount,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                errorSummary = SchedulerStatusClassifier.SanitizeErrorSummary(ex);
                cycleCompleted = DateTimeOffset.UtcNow;
                _log.LogError(ex, "Schedule dispatch cycle failed. Plane={Plane} HostId={HostId}", planeValue, hostId);

                try
                {
                    await using var errScope = _scopeFactory.CreateAsyncScope();
                    var heartbeatStore = errScope.ServiceProvider.GetRequiredService<ISchedulerDispatchHeartbeatStore>();
                    await TryWriteHeartbeatAsync(
                        heartbeatStore,
                        planeValue,
                        hostId,
                        (int)interval.TotalSeconds,
                        cycleStarted,
                        cycleCompleted,
                        lastSuccessful: null,
                        errorSummary,
                        assignmentCount,
                        dueCount,
                        staleCount,
                        candidateCount,
                        claimedCount,
                        dispatchedCount,
                        skippedCount,
                        stoppingToken);
                }
                catch (Exception heartbeatEx)
                {
                    _log.LogWarning(heartbeatEx, "Failed to record failed-cycle scheduler heartbeat. Plane={Plane}", planeValue);
                }
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task TryWriteHeartbeatAsync(
        ISchedulerDispatchHeartbeatStore store,
        string planeValue,
        string hostId,
        int pollIntervalSeconds,
        DateTimeOffset lastStarted,
        DateTimeOffset? lastCompleted,
        DateTimeOffset? lastSuccessful,
        string? errorSummary,
        int assignmentCount,
        int dueCount,
        int staleCount,
        int candidateCount,
        int claimedCount,
        int dispatchedCount,
        int skippedCount,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.UpsertAsync(
                new SchedulerDispatchHeartbeat(
                    planeValue,
                    hostId,
                    pollIntervalSeconds,
                    lastStarted,
                    lastCompleted,
                    lastSuccessful,
                    errorSummary,
                    assignmentCount,
                    dueCount,
                    staleCount,
                    candidateCount,
                    claimedCount,
                    dispatchedCount,
                    skippedCount,
                    DateTimeOffset.UtcNow,
                    WorkerRuntimeHeartbeatMetadata.TryGetProcessStartedUtc(),
                    WorkerRuntimeHeartbeatMetadata.TryGetApplicationVersion(),
                    WorkerRuntimeHeartbeatMetadata.TryGetEnvironmentName()),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                ex,
                "Best-effort scheduler heartbeat write failed. Plane={Plane} HostId={HostId}",
                planeValue,
                hostId);
        }
    }
}
