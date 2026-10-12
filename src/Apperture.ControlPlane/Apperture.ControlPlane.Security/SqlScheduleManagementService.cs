using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// SQL-backed schedule management. Epic 28, Epic 36. Epic 66: lifecycle evidence on write paths. Every mutation is one audited
/// unit (AUD-1.4): create with its assignment, update, enable, disable, assign and the hard-delete each commit with their one
/// audit row or not at all.
/// </summary>
public sealed class SqlScheduleManagementService : IScheduleManagementService
{
    private readonly IScheduleRepository _repo;
    private readonly ISchedulerClaimOptions _claimOptions;
    private readonly TimeProvider _timeProvider;
    private readonly IResourceContextService _resourceContexts;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly AppertureDbContext _db;
    private readonly IResourceOwnershipMutationGate _ownershipGate;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;

    public SqlScheduleManagementService(
        IScheduleRepository repo,
        ISchedulerClaimOptions claimOptions,
        TimeProvider timeProvider,
        IResourceContextService resourceContexts,
        IIntegrationRepository integrationRepository,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate,
        IPrincipalContext principalContext,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null)
    {
        _repo = repo;
        _claimOptions = claimOptions;
        _timeProvider = timeProvider;
        _resourceContexts = resourceContexts;
        _integrationRepository = integrationRepository;
        _db = db;
        _ownershipGate = ownershipGate;
        _principalContext = principalContext;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Schedule changes require the audited unit of work.");

    public async ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
        await _repo.GetDefinitionAsync(id, cancellationToken);

    public async ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
        await _repo.ListDefinitionsAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default) =>
        await _repo.ListDefinitionsAsync(resourceContextId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
        CreateAsync(definition, runtimePlaneId: null, policyOverrides: null, cancellationToken);

    /// <inheritdoc />
    public ValueTask<ScheduleDefinition> CreateScheduleWithAssignmentAsync(
        ScheduleDefinition definition,
        RuntimePlaneId runtimePlaneId,
        string? policyOverrides,
        CancellationToken cancellationToken = default) =>
        CreateAsync(definition, runtimePlaneId, policyOverrides, cancellationToken);

    /// <summary>
    /// One audited unit (AUD-1.4 slice 4): the definition, its initial assignment when a runtime plane is named, and the one
    /// <c>schedule.created</c> row commit together or not at all, so a failure between the two can no longer leave a schedule
    /// with no assignment that is never dispatched. The schedule id comes from the caller, so a rerun after a confirmed rollback
    /// creates the same schedule and a verified commit after a lost acknowledgement is reconstructed from it.
    /// </summary>
    private async ValueTask<ScheduleDefinition> CreateAsync(
        ScheduleDefinition definition,
        RuntimePlaneId? runtimePlaneId,
        string? policyOverrides,
        CancellationToken cancellationToken)
    {
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ScheduleCreated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                ResourceContextId owningContextId;
                if (definition.IntegrationId is null)
                {
                    // Standalone: inherits from nothing, so it asserts no cross-context relationship with respect to the BINDING
                    // invariant, and this path is unchanged and ungated with respect to that invariant. This is not a blanket
                    // proof that no gate is ever needed here: a concurrent standalone-schedule-creation-into-context-T race
                    // against archiving T is a real, pre-existing gap in the ARCHIVE invariant ("Application owns nothing
                    // live"), since an Enabled: true standalone schedule created into T does count toward what blocks T's
                    // archive. That race is out of scope here; leaving it unfixed is a known gap, not an oversight.
                    owningContextId = await ResolveOwningResourceContextAsync(definition, integration: null, ct).ConfigureAwait(false);
                }
                else
                {
                    // Attach path: the new Schedule inherits the Integration's ownership, so this IS an ownership-sensitive
                    // mutation. Gate first, then read the Integration under a row lock, so a concurrent Integration move cannot
                    // commit a new ResourceContextId between the read and the insert, and an Integration delete that counts its
                    // blockers either sees this schedule or finds the integration locked behind it.
                    await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);
                    var integration = await _integrationRepository.GetByIdForUpdateAsync(definition.IntegrationId, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"Integration {definition.IntegrationId.Value} not found.");
                    owningContextId = await ResolveOwningResourceContextAsync(definition, integration, ct).ConfigureAwait(false);
                }

                var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(
                    null, definition with { ResourceContextId = owningContextId }, _timeProvider.GetUtcNow());
                await transaction.StageAsync(
                    AuditEvents.ScheduleCreated(
                        merged.Id.Value,
                        merged.Name,
                        merged.IntegrationId?.Value,
                        merged.Enabled,
                        runtimePlaneId?.Value,
                        runtimePlaneId is null ? 0 : 1,
                        owningContextId.Value),
                    ct).ConfigureAwait(false);
                await _repo.AddDefinitionAsync(merged, ct).ConfigureAwait(false);
                if (runtimePlaneId is not null)
                {
                    await _repo.AddAssignmentAsync(
                        new ScheduleAssignment(
                            new ScheduleAssignmentId(Guid.NewGuid().ToString("N")[..24]),
                            merged.Id,
                            runtimePlaneId,
                            policyOverrides,
                            DesiredVersion: 1,
                            AppliedVersion: 0),
                        ct).ConfigureAwait(false);
                }

                return merged;
            },
            async (committed, ct) => await _repo.GetDefinitionAsync(definition.Id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Schedule {definition.Id.Value} could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RequireActorAsync(CancellationToken cancellationToken)
    {
        // Authorization for schedules is instance-scoped schedule.manage, enforced by the controller before the service runs. A
        // missing actor still fails here, before any read or audit, so a direct caller cannot change anything anonymously. The
        // row's actor is the unit's, resolved from the same principal; nothing a caller supplies is recorded.
        if (await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false) is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
    }


    /// <summary>
    /// Authoritative Schedule ownership rule. See <see cref="IScheduleManagementService.CreateScheduleAsync"/>.
    /// </summary>
    /// <param name="integration">
    /// The already-locked Integration for the attach path (loaded by the caller under
    /// <see cref="IIntegrationRepository.GetByIdForUpdateAsync"/>, inside the ownership gate), or null for
    /// the standalone path, which has no Integration to inherit from.
    /// </param>
    private async ValueTask<ResourceContextId> ResolveOwningResourceContextAsync(
        ScheduleDefinition definition,
        Apperture.Core.Domain.Integration.Integration? integration,
        CancellationToken cancellationToken)
    {
        if (integration is null)
        {
            var standaloneContext = await _resourceContexts
                .ResolveForNewOwnershipAsync(definition.ResourceContextId, cancellationToken)
                .ConfigureAwait(false);
            return standaloneContext.Id;
        }

        if (definition.ResourceContextId is null)
        {
            if (definition.Enabled)
            {
                await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                    integration.ResourceContextId, _resourceContexts, "creating an enabled schedule for this integration", cancellationToken).ConfigureAwait(false);
            }
            return integration.ResourceContextId;
        }

        if (!string.Equals(definition.ResourceContextId.Value, integration.ResourceContextId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Schedule ResourceContext '{definition.ResourceContextId.Value}' does not match the owning Integration's ResourceContext '{integration.ResourceContextId.Value}'. An Integration-attached schedule belongs to its Integration's ResourceContext.");
        }

        var resolved = await _resourceContexts
            .ResolveForNewOwnershipAsync(definition.ResourceContextId, cancellationToken)
            .ConfigureAwait(false);
        return resolved.Id;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>Only the fields a PATCH can change are applied (name, recurrence, time zone, enabled), over the schedule as locked;
    /// the target, execution policy, integration and ownership are never rewritten by an update. Established PATCH semantics are
    /// otherwise unchanged. See <see cref="ChangeAsync"/>.</para>
    /// </remarks>
    public ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            definition.Id,
            AuditActionNames.ScheduleUpdated,
            // The proposed flag is the request's, so it decides the gate; the stored flag is unknown until the row is locked.
            takesGate: definition.Enabled,
            fresh => fresh with
            {
                Name = definition.Name,
                RecurrenceExpression = definition.RecurrenceExpression,
                TimeZone = definition.TimeZone,
                Enabled = definition.Enabled,
            },
            "enabling this schedule",
            cancellationToken);

    /// <inheritdoc />
    public ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
        // Reactivation guard (§6.5, D2): while an Application ResourceContext is archived, nothing it owns may become live
        // again. Gated against a concurrent Archive (CTX-3B follow-on).
        ChangeAsync(scheduleId, AuditActionNames.ScheduleEnabled, takesGate: true, fresh => fresh with { Enabled = true }, "enabling this schedule", cancellationToken);

    /// <inheritdoc />
    public ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
        ChangeAsync(scheduleId, AuditActionNames.ScheduleDisabled, takesGate: false, fresh => fresh with { Enabled = false }, "disabling this schedule", cancellationToken);

    /// <summary>
    /// One audited unit for an update, enable or disable (AUD-1.4 slice 4). Nothing is read before the unit, so a request that
    /// must wait for the ownership gate waits there first, as it always did. Inside the attempt, in this order: the ownership
    /// gate when the change may enable the
    /// schedule (an enabled schedule is archive-blocking, and the stored flag is unknown until the row is locked, so the request
    /// decides), the schedule row, locked to the end of the transaction (the row a delete and a move also take), the change
    /// computed against it, the reactivation guard against its owning context, then the one primary row and the write.
    /// </summary>
    /// <remarks>
    /// A request that changes nothing declares a no-op and records nothing. A request that changes the enabled flag is recorded
    /// under <c>schedule.enabled</c> or <c>schedule.disabled</c>, with the names of any other fields it changed; otherwise under
    /// <c>schedule.updated</c>. Names, never values: the recurrence and time zone have no field. The lifecycle timestamps are
    /// computed by the same merge as before, in the same save.
    /// </remarks>
    private async ValueTask ChangeAsync(
        ScheduleId scheduleId,
        string primaryAction,
        bool takesGate,
        Func<ScheduleDefinition, ScheduleDefinition> propose,
        string actionPhrase,
        CancellationToken cancellationToken)
    {
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;

        await unit.ExecuteAsync(
            new AuditedOperation(primaryAction, AuditActor.CurrentPrincipal, AuditActionNames.ScheduleUpdateActions),
            async (transaction, ct) =>
            {
                if (takesGate)
                    await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                var fresh = await _repo.GetByIdForUpdateAsync(scheduleId, ct).ConfigureAwait(false)
                    ?? throw new ScheduleNotFoundException(scheduleId);

                var proposed = propose(fresh);
                var changed = new List<string>();
                if (!string.Equals(fresh.Name, proposed.Name, StringComparison.Ordinal))
                    changed.Add("name");
                if (!string.Equals(fresh.RecurrenceExpression, proposed.RecurrenceExpression, StringComparison.Ordinal))
                    changed.Add("recurrence");
                if (!string.Equals(fresh.TimeZone, proposed.TimeZone, StringComparison.Ordinal))
                    changed.Add("timeZone");
                var enabledChanges = fresh.Enabled != proposed.Enabled;

                if (!enabledChanges && changed.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                // Flipping Enabled false -> true makes anything newly live, so the owning context must be Active. It is the
                // freshly locked schedule's context, never one the caller supplied.
                if (!fresh.Enabled && proposed.Enabled && fresh.ResourceContextId is { } owningContextId)
                {
                    await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                        owningContextId, _resourceContexts, actionPhrase, ct).ConfigureAwait(false);
                }

                var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(fresh, proposed, _timeProvider.GetUtcNow());
                var resourceContextId = fresh.ResourceContextId?.Value;
                var auditEvent = enabledChanges
                    ? AuditEvents.ScheduleEnabledChanged(
                        proposed.Enabled, scheduleId.Value, merged.Name, merged.IntegrationId?.Value, changed, resourceContextId)
                    : AuditEvents.ScheduleUpdated(scheduleId.Value, merged.Name, merged.IntegrationId?.Value, changed, resourceContextId);
                await transaction.StageAsync(auditEvent, ct).ConfigureAwait(false);
                await _repo.UpdateDefinitionAsync(merged, ct).ConfigureAwait(false);
                return true;
            },
            // A verified commit is the change having happened; the callers return nothing.
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit (AUD-1.4 slice 4): the assignment and its <c>schedule.assigned</c> row commit together. The schedule row
    /// is locked first, the lock a delete also takes, so an assignment is never inserted for a schedule a delete has decided to
    /// remove. Reachable only by a direct caller of the service: the HTTP routes assign through
    /// <see cref="CreateScheduleWithAssignmentAsync"/>.
    /// </remarks>
    public async ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default)
    {
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;
        var id = new ScheduleAssignmentId(Guid.NewGuid().ToString("N")[..24]);

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ScheduleAssigned, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var schedule = await _repo.GetByIdForUpdateAsync(scheduleId, ct).ConfigureAwait(false)
                    ?? throw new ScheduleNotFoundException(scheduleId);

                var assignment = new ScheduleAssignment(id, scheduleId, runtimePlaneId, policyOverrides, DesiredVersion: 1, AppliedVersion: 0);
                await transaction.StageAsync(
                    AuditEvents.ScheduleAssigned(scheduleId.Value, runtimePlaneId.Value, policyOverrides is not null, schedule.ResourceContextId?.Value),
                    ct).ConfigureAwait(false);
                await _repo.AddAssignmentAsync(assignment, ct).ConfigureAwait(false);
                return assignment;
            },
            async (committed, ct) => await _repo.GetAssignmentAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Assignment {id.Value} could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }


    public async ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default)
    {
        return await _repo.GetAssignmentsAsync(runtimePlaneId, cancellationToken);
    }

    public async ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken cancellationToken = default)
    {
        var def = await _repo.GetDefinitionAsync(id, cancellationToken);
        if (def is null)
            return null;
        var assignments = await _repo.GetAssignmentsAsync(planeId, cancellationToken);
        var assignment = assignments.FirstOrDefault(a => a.ScheduleId.Value == id.Value);
        if (assignment is null)
            return new ScheduleWithExecutionState(def, null);
        var state = await _repo.GetStateAsync(assignment.Id.Value, cancellationToken);
        ScheduleInProgressClaim? claim = null;
        var claimInfo = await _repo.GetInProgressRunClaimAsync(assignment.Id.Value, cancellationToken);
        if (claimInfo.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_claimOptions.ClaimLeaseMinutes);
            claim = new ScheduleInProgressClaim(
                claimInfo.Value.ClaimedByHostId,
                claimInfo.Value.StartedAtUtc,
                claimInfo.Value.StartedAtUtc < cutoff,
                claimInfo.Value.ResourceContextId);
        }
        return new ScheduleWithExecutionState(def, state, claim);
    }

    /// <inheritdoc />
    public async ValueTask DeleteScheduleAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default)
    {
        // Authorization for schedules is instance-scoped schedule.manage, enforced by the controller
        // before this method runs (same as enable/disable). A missing actor still fails here, before
        // any read or audit, so a direct caller cannot delete anonymously. The row's actor is the unit's, resolved from the
        // same principal; nothing a caller supplies is recorded.
        if (await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false) is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        // One audited unit (AUD-1.4, gate §13.6): the unit owns the execution strategy and the transaction, and its first
        // statement is the operation lock. Lock order inside it: that lock, the ownership gate, the schedule row, then the
        // graph removal and the primary row. The removal and `schedule.deleted` commit together or not at all.
        var attempt = await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ScheduleDeleted, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Same application lock Enable uses. Enable cannot commit Enabled=true between this
                // acquisition and the locked re-read, and this delete cannot remove a row Enable has
                // already decided to turn on.
                await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                var fresh = await _repo.GetByIdForUpdateAsync(scheduleId, ct).ConfigureAwait(false);
                if (fresh is null)
                {
                    transaction.DeclareNoOp();
                    return ScheduleDeleteAttempt.NotFound;
                }

                if (fresh.Enabled)
                {
                    transaction.DeclareNoOp();
                    return ScheduleDeleteAttempt.Refused(fresh);
                }

                // Archived owning contexts are allowed. Delete does not call the reactivation guard:
                // removing a disabled schedule does not make anything live.
                var removed = await _repo.DeleteDisabledScheduleGraphAsync(scheduleId, ct).ConfigureAwait(false);
                if (!removed.Deleted)
                {
                    // Nothing was removed or staged. The row lock makes this unreachable in practice; if it happens the
                    // schedule changed under us and the answer is whatever the fresh read says.
                    transaction.DeclareNoOp();
                    var again = await _repo.GetDefinitionAsync(scheduleId, ct).ConfigureAwait(false);
                    return again is { Enabled: true } ? ScheduleDeleteAttempt.Refused(again) : ScheduleDeleteAttempt.NotFound;
                }

                await transaction.StageAsync(
                    AuditEvents.ScheduleDeleted(
                        fresh.Id.Value,
                        fresh.Name,
                        fresh.IntegrationId?.Value,
                        removed.AssignmentsRemoved,
                        fresh.ResourceContextId?.Value),
                    ct).ConfigureAwait(false);
                return ScheduleDeleteAttempt.Removed;
            },
            // A verified commit is a successful delete, built from the committed primary row alone. The schedule is gone by
            // design, so its absence is never read as a failed or missing delete.
            (_, _) => Task.FromResult(ScheduleDeleteAttempt.Removed),
            cancellationToken).ConfigureAwait(false);

        switch (attempt.Kind)
        {
            case ScheduleDeleteAttemptKind.NotFound:
                throw new ScheduleNotFoundException(scheduleId);
            case ScheduleDeleteAttemptKind.Enabled:
                // A W3 row of the same principal, written after the attempt's transaction ended. A failure to write it is
                // logged by the writer and never changes the refusal.
                if (_auditEvents is not null)
                {
                    var definition = attempt.Definition!;
                    await _auditEvents.TryWriteAsync(
                        AuditEvents.ScheduleDeleteRejected(
                            definition.Id.Value,
                            definition.Name,
                            definition.IntegrationId?.Value,
                            definition.ResourceContextId?.Value),
                        AuditActor.CurrentPrincipal,
                        CancellationToken.None).ConfigureAwait(false);
                }

                throw new ScheduleDeleteRejectedException(scheduleId);
            case ScheduleDeleteAttemptKind.Deleted:
                return;
            default:
                throw new AuditInvariantException($"Unexpected schedule delete outcome for {scheduleId.Value}.");
        }
    }

    private enum ScheduleDeleteAttemptKind
    {
        NotFound,
        Enabled,
        Deleted
    }

    private sealed record ScheduleDeleteAttempt(ScheduleDeleteAttemptKind Kind, ScheduleDefinition? Definition)
    {
        public static ScheduleDeleteAttempt NotFound { get; } = new(ScheduleDeleteAttemptKind.NotFound, null);
        public static ScheduleDeleteAttempt Removed { get; } = new(ScheduleDeleteAttemptKind.Deleted, null);
        public static ScheduleDeleteAttempt Refused(ScheduleDefinition definition) => new(ScheduleDeleteAttemptKind.Enabled, definition);
    }
}
