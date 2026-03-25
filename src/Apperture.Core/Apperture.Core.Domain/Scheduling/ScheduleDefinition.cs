using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// Canonical schedule metadata and recurrence. Aggregate root.
/// Epic 28: Target added for workflow/operation dispatch.
/// Epic 50: IntegrationId when schedule is owned by an Integration.
/// Epic 66: optional persisted lifecycle evidence for grace correctness.
/// </summary>
public sealed record ScheduleDefinition(
    ScheduleId Id,
    string Name,
    string RecurrenceExpression,
    string TimeZone,
    bool Enabled,
    ScheduledTarget Target,
    string? ExecutionPolicyRef,
    int Version,
    IntegrationId? IntegrationId = null,
    ScheduleLifecycleEvidence? Lifecycle = null);
