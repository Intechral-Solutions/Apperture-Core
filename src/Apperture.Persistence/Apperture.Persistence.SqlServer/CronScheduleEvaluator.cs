using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Cronos;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Cron-based schedule evaluator. Epic 28.
/// </summary>
public sealed class CronScheduleEvaluator : IScheduleEvaluator
{
    private readonly IScheduleRepository _repo;

    public CronScheduleEvaluator(IScheduleRepository repo) => _repo = repo;

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

            if (definition.Target.JobType != ScheduledJobType.WorkflowExecution)
                continue; // V1: workflow only

            var state = await _repo.GetStateAsync(assignment.Id.Value, cancellationToken);
            var fromUtc = state?.LastAttemptedUtc?.AddSeconds(1) ?? asOfUtc.AddDays(-1);

            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZone);
            }
            catch
            {
                tz = TimeZoneInfo.Utc;
            }

            CronExpression cron;
            try
            {
                cron = CronExpression.Parse(definition.RecurrenceExpression);
            }
            catch
            {
                continue;
            }

            var occurrences = cron.GetOccurrences(fromUtc.UtcDateTime, asOfUtc.UtcDateTime, tz);
            foreach (var occ in occurrences)
            {
                var fireUtc = new DateTimeOffset(occ, TimeSpan.Zero);
                var alreadyProcessed = await _repo.RunExistsAsync(assignment.ScheduleId.Value, fireUtc, cancellationToken);
                if (!alreadyProcessed)
                    results.Add(new ScheduleFireCandidate(assignment.ScheduleId.Value, assignment.Id.Value, fireUtc));
            }
        }

        return results;
    }
}
