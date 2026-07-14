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
    /// Bounded lookback when no prior state exists (first-run).
    /// Prevents accidental burst dispatch of many missed runs.
    /// Only occurrences within this window are considered due.
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
            // First-run: bounded lookback to prevent burst dispatch. Subsequent runs: from last attempted + 1s.
            var fromUtc = state?.LastAttemptedUtc?.AddSeconds(1) ?? asOfUtc.AddMinutes(-FirstRunLookbackMinutes);

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

            foreach (var occ in occurrences)
            {
                var fireUtc = ScheduleTimeZoneResolver.ToNormalizedFireUtc(occ);
                var alreadyProcessed = await _repo.RunExistsAsync(assignment.ScheduleId.Value, fireUtc, cancellationToken);
                if (!alreadyProcessed)
                    results.Add(new ScheduleFireCandidate(assignment.ScheduleId.Value, assignment.Id.Value, fireUtc));
            }
        }

        return results;
    }
}
