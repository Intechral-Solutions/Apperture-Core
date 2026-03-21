using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Computes due schedule firings. Epic 28.
/// </summary>
public interface IScheduleEvaluator
{
    /// <summary>
    /// Gets schedule firings that are due for the given runtime plane at or before the given time.
    /// </summary>
    ValueTask<IReadOnlyList<ScheduleFireCandidate>> GetDueAsync(
        RuntimePlaneId runtimePlaneId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A schedule firing that is due. Epic 28.
/// </summary>
/// <param name="ScheduleId">Schedule definition id.</param>
/// <param name="AssignmentId">Assignment id.</param>
/// <param name="FireTimeUtc">Computed fire time (cron-based).</param>
public sealed record ScheduleFireCandidate(
    string ScheduleId,
    string AssignmentId,
    DateTimeOffset FireTimeUtc);
