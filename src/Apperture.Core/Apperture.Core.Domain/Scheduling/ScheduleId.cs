using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// Unique identifier for a Schedule Definition.
/// </summary>
public sealed record ScheduleId(string Value) : EntityId(Value);
