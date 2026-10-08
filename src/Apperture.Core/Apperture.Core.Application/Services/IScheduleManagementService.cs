using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;

namespace Apperture.Core.Application.Services;

/// <summary>
/// CRUD and assignment of schedules (control plane). Policy and version sync.
/// </summary>
public interface IScheduleManagementService
{
    /// <summary>
    /// Gets a schedule definition by id.
    /// </summary>
    ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all schedule definitions.
    /// </summary>
    ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists schedule definitions, optionally restricted to one owning ResourceContext.
    /// </summary>
    /// <param name="resourceContextId">
    /// Owning ResourceContext to filter by, or null for the default global behavior (no context filter).
    /// </param>
    /// <remarks>
    /// The default implementation exists only so in-memory test doubles keep working; the production
    /// implementation pushes the filter down to the repository query.
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
    /// Creates a schedule definition and resolves its owning ResourceContext.
    /// </summary>
    /// <remarks>
    /// This is the authoritative ownership rule for Schedules; controllers must not reimplement it.
    /// <list type="bullet">
    /// <item>Standalone schedule, no context supplied: owned by the reserved Shared context.</item>
    /// <item>Standalone schedule, context supplied: that context, which must exist and be Active.</item>
    /// <item>Integration-attached, no context supplied: inherits the Integration's ResourceContext.</item>
    /// <item>Integration-attached, context supplied: must equal the Integration's, otherwise rejected.</item>
    /// </list>
    /// An Integration-attached schedule is never blindly defaulted to Shared.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The supplied context does not exist, is archived, or disagrees with the Integration's context.
    /// </exception>
    ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a schedule definition.
    /// </summary>
    ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a schedule to a runtime plane.
    /// </summary>
    ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a schedule.
    /// </summary>
    ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a schedule.
    /// </summary>
    ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes a disabled schedule, its assignments, and those assignments' schedule-state cursors.
    /// Historical firings and run evidence are retained.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">No authenticated actor.</exception>
    /// <exception cref="ScheduleDeleteRejectedException">The schedule is still enabled.</exception>
    /// <exception cref="ScheduleNotFoundException">The schedule does not exist.</exception>
    /// <remarks>
    /// <para>The production delete is one audited unit (AUD-1.4): the removal and its <c>schedule.deleted</c> row commit together
    /// or not at all, and an uncertain commit is verified and either reconstructed as success or reported as
    /// <c>outcome_unknown</c>. A refusal for an enabled schedule is a separate best-effort <c>schedule.delete_rejected</c> row.
    /// Failures of the unit itself are not "not found" and are not <see cref="ScheduleNotFoundException"/>.</para>
    /// <para>The default throws so in-memory test doubles keep compiling. The production service implements the delete.</para>
    /// </remarks>
    ValueTask DeleteScheduleAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("DeleteScheduleAsync is not implemented by this schedule service.");

    /// <summary>
    /// Gets schedule assignments for a runtime plane.
    /// </summary>
    ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a schedule definition with execution state for the given runtime plane.
    /// Used to expose last attempted, last outcome, and blocked reason to operators.
    /// </summary>
    ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Schedule definition plus execution state for operator visibility. Epic 28, Epic 36 (claim visibility).
/// </summary>
public sealed record ScheduleWithExecutionState(
    ScheduleDefinition Definition,
    ScheduleState? ExecutionState,
    ScheduleInProgressClaim? InProgressClaim = null);

/// <summary>
/// Active scheduler claim for the assignment: present only when a <c>ScheduleRuns</c> row exists with
/// <c>Outcome = InProgress</c> for this assignment. Not populated for historical completed runs.
/// Epic 36 corrective pass: operators must not read this as “last run was claimed by X.”
/// </summary>
/// <param name="ClaimedByHostId">Host that currently owns the in-progress claim (may be empty for pre-migration rows).</param>
/// <param name="ClaimedAtUtc">Lease clock: same as run <c>StartedAtUtc</c> for the active in-progress row.</param>
/// <param name="IsStale">True when <paramref name="ClaimedAtUtc"/> is before <c>UtcNow - ClaimLeaseMinutes</c>; another host may recover.</param>
public sealed record ScheduleInProgressClaim(
    string ClaimedByHostId,
    DateTimeOffset ClaimedAtUtc,
    bool IsStale,
    /// <summary>
    /// CTX-2: immutable ResourceContext snapshot from the specific ScheduleRun this claim belongs to
    /// (copied at claim time from ScheduleDefinition.ResourceContextId). Distinct from the Schedule's
    /// current ownership, which the caller already has on hand as ScheduleDefinition.ResourceContextId.
    /// </summary>
    string? ResourceContextId = null);
