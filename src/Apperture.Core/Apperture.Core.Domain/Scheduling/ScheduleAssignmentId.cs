using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// Unique identifier for a Schedule Assignment (schedule bound to a Runtime Plane).
/// </summary>
public sealed record ScheduleAssignmentId(string Value) : EntityId(Value);
