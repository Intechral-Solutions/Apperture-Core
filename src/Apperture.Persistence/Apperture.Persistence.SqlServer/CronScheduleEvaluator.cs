using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Cronos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Cron-based schedule evaluator. Epic 28 / SWM-3 timezone semantics.
/// </summary>
public sealed class CronScheduleEvaluator : IScheduleEvaluator
{
    /// <summary>
    /// First-run lookback when no <c>LastAttemptedUtc</c> exists. Bounds cold-start so a new
    /// high-frequency schedule does not claim a large historical window in one cycle.
    /// Subsequent evaluation uses <c>LastAttemptedUtc+1s</c> and returns at most one fire
    /// (catch-up drains across polls — never burst-dispatches every missed minute).
    /// </summary>
    internal const int FirstRunLookbackMinutes = 5;

    private readonly IScheduleRepository _repo;
    private readonly ILogger<CronScheduleEvaluator> _log;

    public CronScheduleEvaluator(IScheduleRepository repo, ILogger<CronScheduleEvaluator>? log = null)
    {
        _repo = repo;
        _log = log ?? NullLogger<CronScheduleEvaluator>.Instance;
    }

    public async ValueTask<IReadOnlyList<ScheduleFireCandidate>> GetDueAsync(
        RuntimePlaneId runtimePlaneId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var assignments = await _repo.GetAssignmentsAsync(runtimePlaneId, cancellationToken);
        var results = new List<ScheduleFireCandidate>();

        foreach (var assignment in assignments)
        {
            var definition = await _repo.GetDefinitionAsync(assignment.ScheduleId, cancellationToken);
            if (definition is null || !definition.Enabled)
                continue;

            if (definition.Target.JobType != ScheduledJobType.WorkflowExecution &&
                definition.Target.JobType != ScheduledJobType.ModuleOperationExecution)
                continue; // V1: workflow and operation only

            var state = await _repo.GetStateAsync(assignment.Id.Value, cancellationToken);
            // First-run: bounded lookback. Subsequent: from LastAttempted+1s (catch-up is
            // serialized by returning at most one fire below — do not floor away older due
            // instants or they are permanently skipped).
            var fromUtc = state?.LastAttemptedUtc?.AddSeconds(1)
                ?? asOfUtc.AddMinutes(-FirstRunLookbackMinutes);

            var tz = ScheduleTimeZoneResolver.ResolvePersistedOrUtc(definition.TimeZone, _log);

            CronExpression cron;
            try
            {
                cron = CronExpression.Parse(definition.RecurrenceExpression);
            }
            catch
            {
                continue;
            }

            List<DateTime> occurrences;
            try
            {
                occurrences = cron.GetOccurrences(fromUtc.UtcDateTime, asOfUtc.UtcDateTime, tz).ToList();
            }
            catch (ArgumentException ex) when (ex.ParamName == "fromUtc")
            {
                // Cronos requires fromUtc < toUtc (strict). Skip this assignment when LastAttemptedUtc+1s meets or exceeds asOfUtc.
                continue;
            }

            // At most one due fire per schedule per evaluation cycle (earliest unclaimed).
            // Further missed instants drain on later polls — never burst-dispatch.
            ScheduleFireCandidate? nextDue = null;
            foreach (var occ in occurrences.OrderBy(o => o))
            {
                var fireUtc = ScheduleTimeZoneResolver.ToNormalizedFireUtc(occ);
                if (await _repo.RunExistsAsync(assignment.ScheduleId.Value, fireUtc, cancellationToken))
                    continue;

                nextDue = new ScheduleFireCandidate(assignment.ScheduleId.Value, assignment.Id.Value, fireUtc);
                break;
            }

            if (nextDue is not null)
                results.Add(nextDue);
        }

        return results;
    }
}
