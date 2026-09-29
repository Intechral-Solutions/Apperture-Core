using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
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
    /// Lists schedule definitions, optionally restricted to one owning ResourceContext.
    /// </summary>
    /// <param name="resourceContextId">
    /// Owning ResourceContext to filter by, or null for the default global behavior (no context filter).
    /// </param>
    /// <remarks>
    /// A store-backed implementation must push the context filter into the query. The default
    /// implementation here exists only so in-memory test doubles keep working; it is not a query plan.
    /// </remarks>
    async ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default)
    {
        var all = await ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        if (resourceContextId is null)
            return all;
        return all.Where(d => string.Equals(d.ResourceContextId?.Value, resourceContextId.Value, StringComparison.Ordinal)).ToList();
    }

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
    /// Claims a fire and snapshots <paramref name="resourceContextId"/> onto the ScheduleRun row at
    /// claim time (not at successful dispatch). CTX-2: the caller resolves this once, from the
    /// ScheduleDefinition's own ResourceContext; this method does not derive it. Never rewritten
    /// after the row is written, including on stale-claim recovery.
    /// </summary>
    /// <remarks>
    /// The default implementation exists only so in-memory test doubles keep working; it ignores
    /// the context. The production implementation snapshots it.
    /// </remarks>
    async ValueTask<bool> TryClaimFireAsync(
        string scheduleId,
        DateTimeOffset fireTimeUtc,
        string assignmentId,
        string hostId,
        string? resourceContextId,
        CancellationToken cancellationToken = default) =>
        await TryClaimFireAsync(scheduleId, fireTimeUtc, assignmentId, hostId, cancellationToken).ConfigureAwait(false);

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
    ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc, string? ResourceContextId)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken cancellationToken = default);

    ValueTask CompleteRunAsync(string scheduleId, DateTimeOffset fireTimeUtc, string outcome, string? outcomeReason, string? runId, CancellationToken cancellationToken = default);
    ValueTask UpdateStateAsync(string assignmentId, ScheduleStateUpdate state, CancellationToken cancellationToken = default);
    ValueTask<ScheduleState?> GetStateAsync(string assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a ScheduleDefinition for an ownership-sensitive write, taking a row-level update lock held for
    /// the lifetime of the caller's ambient transaction on this same <c>AppertureDbContext</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Must be called inside an already-open transaction, <b>after</b>
    /// <c>IResourceOwnershipMutationGate.AcquireAsync</c>. The gate is what makes the operation correct;
    /// this row lock is defense in depth against a future path that skips the gate.
    /// </para>
    /// <para>
    /// The result is <b>not tracked</b>. `EnableRetryOnFailure` is active in production, so a rolled-back
    /// attempt must leave nothing in the change tracker for the retried attempt to read back as if it were
    /// current state.
    /// </para>
    /// <para>
    /// On the EF Core InMemory provider (unit tests) this degrades to a plain untracked read with no real
    /// lock — serialization itself can only be proven against real SQL Server.
    /// </para>
    /// </remarks>
    ValueTask<ScheduleDefinition?> GetByIdForUpdateAsync(ScheduleId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes only the owning <c>ResourceContextId</c> column. The only supported way to change a
    /// ScheduleDefinition's ownership (CTX-3B); <see cref="UpdateDefinitionAsync"/> deliberately never
    /// touches this column. Must be called inside the same gated transaction as the preceding
    /// <see cref="GetByIdForUpdateAsync"/>.
    /// </summary>
    /// <remarks>
    /// Implemented with <c>ExecuteUpdateAsync</c> on relational providers, so it bypasses the change
    /// tracker entirely: it writes exactly one column, never flushes unrelated pending changes on the
    /// shared request-scoped context, and leaves no tracked state behind for an execution-strategy retry.
    /// </remarks>
    ValueTask SetResourceContextIdAsync(ScheduleId id, ResourceContextId resourceContextId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a disabled schedule's live graph: schedule-state rows for its assignments, those
    /// assignments, then the definition. Does nothing when the definition is missing or still enabled.
    /// Does not touch <c>ScheduleRuns</c> or any other historical table. There is no foreign key from
    /// assignments, states, or runs to the definition, so this order is the integrity rule.
    /// </summary>
    /// <remarks>
    /// Call inside the same transaction as <see cref="GetByIdForUpdateAsync"/>. The row lock plus the
    /// caller's ownership gate is what makes the enabled check stable. The default throws so test doubles
    /// that never delete keep compiling. The production repository implements the delete.
    /// </remarks>
    ValueTask<DisabledScheduleGraphDeleteResult> DeleteDisabledScheduleGraphAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("DeleteDisabledScheduleGraphAsync is not implemented by this schedule repository.");
}

/// <summary>Result of <see cref="IScheduleRepository.DeleteDisabledScheduleGraphAsync"/>.</summary>
/// <param name="Deleted">True when the disabled definition row was removed.</param>
/// <param name="AssignmentsRemoved">Assignment rows removed with it. Zero when <paramref name="Deleted"/> is false.</param>
public readonly record struct DisabledScheduleGraphDeleteResult(bool Deleted, int AssignmentsRemoved);

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
