using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// Canonical schedule metadata and recurrence. Aggregate root.
/// </summary>
public sealed record ScheduleDefinition(
    ScheduleId Id,
    string Name,
    string RecurrenceExpression,
    string TimeZone,
    bool Enabled,
    string? ExecutionPolicyRef,
    int Version);
