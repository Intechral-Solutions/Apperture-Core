using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Returns active schedule assignments for a Runtime Plane. Used by Scheduler Host (instance-owned scheduling).
/// </summary>
public interface IScheduleProvider
{
    /// <summary>
    /// Gets active schedule assignments for the given runtime plane.
    /// </summary>
    IAsyncEnumerable<ScheduleAssignment> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default);
}
