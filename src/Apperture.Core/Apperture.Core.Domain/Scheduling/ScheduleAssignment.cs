using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// Binds a schedule to a Runtime Plane with policy. Entity.
/// </summary>
public sealed record ScheduleAssignment(
    ScheduleAssignmentId Id,
    ScheduleId ScheduleId,
    RuntimePlaneId RuntimePlaneId,
    string? PolicyOverrides,
    int DesiredVersion,
    int AppliedVersion);
