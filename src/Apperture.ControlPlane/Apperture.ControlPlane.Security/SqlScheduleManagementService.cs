using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// SQL-backed schedule management. Epic 28, Epic 36. Epic 66: lifecycle evidence on write paths.
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
    private readonly IAuditService _audit;

    public SqlScheduleManagementService(
        IScheduleRepository repo,
        ISchedulerClaimOptions claimOptions,
        TimeProvider timeProvider,
        IResourceContextService resourceContexts,
        IIntegrationRepository integrationRepository,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate,
        IPrincipalContext principalContext,
        IAuditService audit)
    {
        _repo = repo;
        _claimOptions = claimOptions;
        _timeProvider = timeProvider;
        _resourceContexts = resourceContexts;
        _integrationRepository = integrationRepository;
        _db = db;
        _ownershipGate = ownershipGate;
        _principalContext = principalContext;
        _audit = audit;
    }

    public async ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
        await _repo.GetDefinitionAsync(id, cancellationToken);

    public async ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
        await _repo.ListDefinitionsAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default) =>
        await _repo.ListDefinitionsAsync(resourceContextId, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default)
    {
        if (definition.IntegrationId is null)
        {
            // Standalone: inherits from nothing, so it asserts no cross-context relationship with respect
            // to the BINDING invariant, and this path is unchanged and ungated with respect to that
            // invariant. This is not a blanket proof that no gate is ever needed here: a concurrent
            // standalone-schedule-creation-into-context-T race against archiving T is a real, pre-existing
            // gap in the ARCHIVE invariant ("Application owns nothing live"), since an Enabled: true
            // standalone schedule created into T does count toward what blocks T's archive. That race is
            // out of scope for this plan; leaving it unfixed here is a known gap for a future plan, not an
            // oversight.
            var owningContextId = await ResolveOwningResourceContextAsync(definition, integration: null, cancellationToken).ConfigureAwait(false);
            var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(null, definition with { ResourceContextId = owningContextId }, _timeProvider.GetUtcNow());
            await _repo.AddDefinitionAsync(merged, cancellationToken);
            return merged;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();

            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Attach path: the new Schedule inherits the Integration's ownership, so this IS an
            // ownership-sensitive mutation. Gate first, then read the Integration under a row lock, so a
            // concurrent Integration move cannot commit a new ResourceContextId between the read and the
            // insert (which would persist a schedule already inconsistent with its own Integration).
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            var integration = await _integrationRepository.GetByIdForUpdateAsync(definition.IntegrationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Integration {definition.IntegrationId.Value} not found.");

            var owningContextId = await ResolveOwningResourceContextAsync(definition, integration, cancellationToken).ConfigureAwait(false);
            var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(null, definition with { ResourceContextId = owningContextId }, _timeProvider.GetUtcNow());
            await _repo.AddDefinitionAsync(merged, cancellationToken).ConfigureAwait(false);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return merged;
        }).ConfigureAwait(false);
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

    public async ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default)
    {
        var previous = await _repo.GetDefinitionAsync(definition.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {definition.Id.Value} not found.");

        // Reactivation guard (§6.5, D2), CTX-3A finding C2: PATCH /schedules/{id} reaches this method
        // directly and previously bypassed EnableAsync's guard entirely, letting an archived
        // Application's schedule be re-enabled through an ordinary field update. Flipping
        // Enabled false -> true here is the exact same reactivation EnableAsync guards, so it gets the
        // same guard. Any other field update (rename, cron change, disabling, staying disabled, or
        // staying enabled) remains allowed regardless of the owning context's archived state, since
        // none of those make anything newly live, so it stays on the ordinary ungated write path below.
        //
        // `definition` is caller-supplied and its ResourceContextId is a freely-settable `init`
        // property (see ResourceMoveDtoImmutabilityTests), so it must never be trusted for the guard
        // check or the persistence merge -- ApplyGatedScheduleTransitionAsync re-reads the schedule
        // fresh, inside the gate, and uses THAT for both (CTX-3B review findings C1/I3).
        if (!previous.Enabled && definition.Enabled)
        {
            await ApplyGatedScheduleTransitionAsync(definition.Id, _ => definition, "enabling this schedule", cancellationToken).ConfigureAwait(false);
            return;
        }

        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(
            previous,
            definition,
            _timeProvider.GetUtcNow());
        await _repo.UpdateDefinitionAsync(merged, cancellationToken);
    }

    /// <summary>
    /// Applies a Schedule field update whose transition can make the owning ResourceContext newly
    /// archive-blocking (Disabled -&gt; Enabled), gated against a concurrent Archive
    /// (CTX-3B follow-on to Plan 2 Task 5). Callers that don't hit this transition should call
    /// <c>_repo.UpdateDefinitionAsync</c> directly and skip this method entirely -- it exists only for
    /// the reactivation-guarded path.
    /// </summary>
    /// <param name="scheduleId">The schedule to transition. Re-read fresh inside the gate -- see below.</param>
    /// <param name="applyChange">
    /// Produces the proposed <see cref="ScheduleDefinition"/> from the freshly re-read definition (e.g.
    /// <c>fresh =&gt; fresh with { Enabled = true }</c> for <see cref="EnableAsync"/>, or <c>_ =&gt;
    /// definition</c> for <see cref="UpdateScheduleAsync"/>, which already has the caller's whole
    /// proposed record). Receives the fresh read, never a pre-gate snapshot.
    /// </param>
    private async ValueTask ApplyGatedScheduleTransitionAsync(
        ScheduleId scheduleId,
        Func<ScheduleDefinition, ScheduleDefinition> applyChange,
        string actionPhrase,
        CancellationToken cancellationToken)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Gate first: enabling a Schedule is an archive-blocking transition, so it must not interleave
            // with a concurrent Archive evaluating the owning context's ownership summary.
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            // Re-read fresh, inside the gate (CTX-3B review findings C1/I3): a pre-gate snapshot, or a
            // caller-supplied ScheduleDefinition's ResourceContextId (a freely-settable `init` property
            // that a hostile or careless caller could tamper with -- see
            // ResourceMoveDtoImmutabilityTests), must never drive the guard check. And the schedule can
            // genuinely have moved between a pre-gate read and gate acquisition (e.g. a concurrent
            // ResourceOwnershipMoveService.MoveScheduleAsync), so only a read taken AFTER the gate is
            // trustworthy. Both the guard below and the persistence merge use this fresh definition.
            var fresh = await _repo.GetDefinitionAsync(scheduleId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Schedule {scheduleId.Value} not found.");

            if (fresh.ResourceContextId is { } owningContextId)
            {
                await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                    owningContextId, _resourceContexts, actionPhrase, cancellationToken).ConfigureAwait(false);
            }

            var proposed = applyChange(fresh);
            var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(fresh, proposed, _timeProvider.GetUtcNow());
            await _repo.UpdateDefinitionAsync(merged, cancellationToken).ConfigureAwait(false);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Same definition row lock delete holds. An assignment insert blocks until that transaction
            // commits, then fails if the definition is gone, so a delete cannot leave a new assignment behind.
            _ = await _repo.GetByIdForUpdateAsync(scheduleId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Schedule {scheduleId.Value} not found.");

            var id = Guid.NewGuid().ToString("N")[..24];
            var assignment = new ScheduleAssignment(
                new ScheduleAssignmentId(id),
                scheduleId,
                runtimePlaneId,
                policyOverrides,
                DesiredVersion: 1,
                AppliedVersion: 0);
            await _repo.AddAssignmentAsync(assignment, cancellationToken).ConfigureAwait(false);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return assignment;
        }).ConfigureAwait(false);
    }

    public async ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default)
    {
        // Reactivation guard (§6.5, D2): while an Application ResourceContext is archived, nothing
        // it owns may become live again. Gated against a concurrent Archive (CTX-3B follow-on to
        // Plan 2 Task 5): the existence check, the authoritative context check, and the actual field
        // change all happen inside the gated helper, against a definition re-read fresh after the gate
        // is acquired (CTX-3B review findings C1/I3).
        await ApplyGatedScheduleTransitionAsync(scheduleId, fresh => fresh with { Enabled = true }, "enabling this schedule", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default)
    {
        var def = await _repo.GetDefinitionAsync(scheduleId, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {scheduleId.Value} not found.");
        var proposed = def with { Enabled = false };
        var merged = ScheduleLifecycleEvidenceApplier.MergeForPersist(def, proposed, _timeProvider.GetUtcNow());
        await _repo.UpdateDefinitionAsync(merged, cancellationToken);
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
        // any read or audit, so a direct caller cannot delete anonymously.
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var strategy = _db.Database.CreateExecutionStrategy();
        var attempt = await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Same application lock Enable uses. Enable cannot commit Enabled=true between this
            // acquisition and the locked re-read, and this delete cannot remove a row Enable has
            // already decided to turn on.
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            var fresh = await _repo.GetByIdForUpdateAsync(scheduleId, cancellationToken).ConfigureAwait(false);
            if (fresh is null)
                return new ScheduleDeleteAttempt(ScheduleDeleteAttemptKind.NotFound, null, 0);

            if (fresh.Enabled)
                return new ScheduleDeleteAttempt(ScheduleDeleteAttemptKind.Enabled, fresh, 0);

            // Archived owning contexts are allowed. Delete does not call the reactivation guard:
            // removing a disabled schedule does not make anything live.
            var removed = await _repo.DeleteDisabledScheduleGraphAsync(scheduleId, cancellationToken).ConfigureAwait(false);
            if (!removed.Deleted)
            {
                var again = await _repo.GetDefinitionAsync(scheduleId, cancellationToken).ConfigureAwait(false);
                if (again is { Enabled: true })
                    return new ScheduleDeleteAttempt(ScheduleDeleteAttemptKind.Enabled, again, 0);
                return new ScheduleDeleteAttempt(ScheduleDeleteAttemptKind.NotFound, null, 0);
            }

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new ScheduleDeleteAttempt(ScheduleDeleteAttemptKind.Deleted, fresh, removed.AssignmentsRemoved);
        }).ConfigureAwait(false);

        // Audit is after the transaction, matching integration.deleted: the SQL delete and the audit
        // append do not share a commit. A lost success-audit row is preferred to refusing a delete
        // that has already committed. A rejection audit failure happens before any row is removed,
        // so the schedule is still enabled and the caller can retry.
        switch (attempt.Kind)
        {
            case ScheduleDeleteAttemptKind.NotFound:
                throw new InvalidOperationException($"Schedule {scheduleId.Value} not found.");
            case ScheduleDeleteAttemptKind.Enabled:
                await AppendDeleteAuditAsync(actor, attempt.Definition!, ActionConstants.ScheduleDeleteRejected, "rejected", assignmentsRemoved: null, cancellationToken).ConfigureAwait(false);
                throw new ScheduleDeleteRejectedException(scheduleId);
            case ScheduleDeleteAttemptKind.Deleted:
                await AppendDeleteAuditAsync(actor, attempt.Definition!, ActionConstants.ScheduleDeleted, "succeeded", attempt.AssignmentsRemoved, cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new InvalidOperationException($"Unexpected schedule delete outcome for {scheduleId.Value}.");
        }
    }

    private async ValueTask AppendDeleteAuditAsync(
        IActor actor,
        ScheduleDefinition definition,
        string action,
        string outcome,
        int? assignmentsRemoved,
        CancellationToken cancellationToken)
    {
        var integrationPhrase = definition.IntegrationId is { } integrationId
            ? $"integration {integrationId.Value}"
            : "no integration";
        var details = assignmentsRemoved is { } count
            ? $"Deleted schedule '{definition.Name}' ({definition.Id.Value}); {integrationPhrase}; removed {count} assignment(s)."
            : $"Cannot delete schedule '{definition.Name}' ({definition.Id.Value}); {integrationPhrase}; it is still enabled.";
        await _audit.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actor.Id,
            action,
            new ResourceDescriptor(ResourceTypeConstants.Schedule, definition.Id.Value),
            Outcome: outcome,
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: details,
            ScheduleId: definition.Id.Value,
            ResourceContextId: definition.ResourceContextId?.Value), cancellationToken).ConfigureAwait(false);
    }

    private enum ScheduleDeleteAttemptKind
    {
        NotFound,
        Enabled,
        Deleted
    }

    private sealed record ScheduleDeleteAttempt(ScheduleDeleteAttemptKind Kind, ScheduleDefinition? Definition, int AssignmentsRemoved);
}
