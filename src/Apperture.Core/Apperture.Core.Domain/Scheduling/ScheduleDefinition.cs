using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;

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
    ScheduleLifecycleEvidence? Lifecycle = null)
{
    /// <summary>
    /// The ResourceContext that owns this Schedule. Required in storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Schedule owns a direct ResourceContext rather than deriving one from its Integration:
    /// standalone Schedules are legal and have no Integration to derive from.
    /// </para>
    /// <para>
    /// <c>null</c> means <i>not yet resolved</i> — the value a caller supplies before the authoritative
    /// schedule application service assigns ownership. Resolution rules: a standalone Schedule defaults
    /// to Shared; an Integration-attached Schedule inherits its Integration's ResourceContext, and an
    /// explicitly supplied context must equal the Integration's or the create is rejected. Schedules
    /// loaded from persistence always carry a value.
    /// </para>
    /// </remarks>
    public ResourceContextId? ResourceContextId { get; init; }
}
