using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Schedule persistence for definitions, assignments, runs, and state. Epic 28, Epic 50.
/// </summary>
public interface IScheduleRepository
{
    ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets schedules owned by the given Integration. Epic 50.
    /// </summary>
    ValueTask<IReadOnlyList<ScheduleDefinition>> GetByIntegrationIdAsync(IntegrationId integrationId, CancellationToken cancellationToken = default);
    ValueTask AddDefinitionAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default);
    ValueTask UpdateDefinitionAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default);
    ValueTask<ScheduleAssignment?> GetAssignmentAsync(ScheduleAssignmentId id, CancellationToken cancellationToken = default);
    ValueTask AddAssignmentAsync(ScheduleAssignment assignment, CancellationToken cancellationToken = default);
    ValueTask UpdateAssignmentAsync(ScheduleAssignment assignment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a run record exists for the given schedule and fire time.
    /// </summary>
    ValueTask<bool> RunExistsAsync(string scheduleId, DateTimeOffset fireTimeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to claim a fire (idempotency). Returns true if claimed, false if already processed.
    /// Epic 36: hostId identifies the claiming scheduler host for multi-host coordination.
    /// </summary>
    ValueTask<bool> TryClaimFireAsync(string scheduleId, DateTimeOffset fireTimeUtc, string assignmentId, string hostId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns candidates for stale-claim recovery: InProgress runs with StartedAtUtc before cutoff, for assignments in the plane.
    /// Epic 36.
    /// </summary>
    ValueTask<IReadOnlyList<ScheduleFireCandidate>> GetRecoverableStaleCandidatesAsync(RuntimePlaneId planeId, DateTimeOffset staleCutoffUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to recover a stale claim. Returns true if this host took over the claim.
    /// Epic 36.
    /// </summary>
    ValueTask<bool> TryRecoverStaleClaimAsync(string scheduleId, DateTimeOffset fireTimeUtc, string assignmentId, string hostId, DateTimeOffset staleCutoffUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns claim projection for an <strong>active</strong> in-progress schedule run only
    /// (<c>Outcome = InProgress</c>). Returns null when no such row exists (including after
    /// <c>CompleteRunAsync</c>). Does not surface historical claim owners on completed runs. Epic 36.
    /// </summary>
    ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken cancellationToken = default);

    ValueTask CompleteRunAsync(string scheduleId, DateTimeOffset fireTimeUtc, string outcome, string? outcomeReason, string? runId, CancellationToken cancellationToken = default);
    ValueTask UpdateStateAsync(string assignmentId, ScheduleStateUpdate state, CancellationToken cancellationToken = default);
    ValueTask<ScheduleState?> GetStateAsync(string assignmentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Schedule run record for persistence. Epic 28.
/// </summary>
public sealed record ScheduleRunRecord(
    string ScheduleId,
    string AssignmentId,
    DateTimeOffset FireTimeUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Outcome,
    string? OutcomeReason,
    string? RunId);

/// <summary>
/// Schedule state update. Epic 28.
/// </summary>
public sealed record ScheduleStateUpdate(
    DateTimeOffset? NextDueUtc,
    DateTimeOffset? LastAttemptedUtc,
    string? LastOutcome,
    string? LastOutcomeReason,
    DateTimeOffset? LastSuccessfulDispatchUtc);

/// <summary>
/// Schedule state for an assignment. Epic 28.
/// </summary>
public sealed record ScheduleState(
    string AssignmentId,
    DateTimeOffset? NextDueUtc,
    DateTimeOffset? LastAttemptedUtc,
    string? LastOutcome,
    string? LastOutcomeReason,
    DateTimeOffset? LastSuccessfulDispatchUtc);
