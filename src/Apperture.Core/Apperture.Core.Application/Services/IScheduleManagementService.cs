using Apperture.Core.Domain.Environment;
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
    /// Creates a schedule definition.
    /// </summary>
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
    bool IsStale);
