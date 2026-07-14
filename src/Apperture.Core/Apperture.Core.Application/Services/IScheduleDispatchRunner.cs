using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Application.Services;

/// <summary>SWM-6: outcome counts from one <see cref="IScheduleDispatchRunner"/> cycle.</summary>
public sealed record ScheduleDispatchCycleResult(
    int ClaimedCount,
    int DispatchedCount,
    int SkippedCount);

/// <summary>
/// Runs one schedule dispatch cycle. Epic 28. Extracted for testability.
/// </summary>
public interface IScheduleDispatchRunner
{
    ValueTask<ScheduleDispatchCycleResult> RunCycleAsync(
        RuntimePlaneId planeId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default);
}
