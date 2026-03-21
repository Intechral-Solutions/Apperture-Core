using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Runs one schedule dispatch cycle. Epic 28. Extracted for testability.
/// </summary>
public interface IScheduleDispatchRunner
{
    ValueTask RunCycleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default);
}
