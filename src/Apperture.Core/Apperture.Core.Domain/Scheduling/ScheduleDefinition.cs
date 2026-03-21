namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// Canonical schedule metadata and recurrence. Aggregate root.
/// Epic 28: Target added for workflow/operation dispatch.
/// </summary>
public sealed record ScheduleDefinition(
    ScheduleId Id,
    string Name,
    string RecurrenceExpression,
    string TimeZone,
    bool Enabled,
    ScheduledTarget Target,
    string? ExecutionPolicyRef,
    int Version);
