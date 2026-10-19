using System.Diagnostics.CodeAnalysis;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Shared.Kernel;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Validates and performs explicit resource-ownership moves (design gate §8). CTX-3B Plan 1 implemented
/// preview validation (<c>Preview*Async</c>); Plan 2 implemented the real transactional move and locking
/// (<c>Move*Async</c>). Both call the same <c>Validate*MoveAsync</c> methods this plan defines, so preview
/// and move cannot drift (§8.2).
/// </summary>
public sealed class ResourceOwnershipMoveService : IResourceOwnershipMoveService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IBindingRepository _bindingRepository;
    private readonly IResourceContextService _resourceContexts;
    private readonly IResourceOwnershipMutationGate _ownershipGate;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;

    public ResourceOwnershipMoveService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrationRepository,
        IConnectionRepository connectionRepository,
        IScheduleRepository scheduleRepository,
        IBindingRepository bindingRepository,
        IResourceContextService resourceContexts,
        IResourceOwnershipMutationGate ownershipGate,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _integrationRepository = integrationRepository;
        _connectionRepository = connectionRepository;
        _scheduleRepository = scheduleRepository;
        _bindingRepository = bindingRepository;
        _resourceContexts = resourceContexts;
        _ownershipGate = ownershipGate;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Ownership moves require the audited unit of work.");

    /// <summary>
    /// Test-only synchronization point, fired inside the move transaction after the gate has been
    /// acquired and validation has run, but before commit. Lets a concurrency test hold one transaction
    /// open at a precisely known moment and observe what a second operation does. Never set in production
    /// (nothing outside the test assemblies can see it).
    /// </summary>
    internal Func<string, CancellationToken, Task>? MidTransactionHook { get; set; }

    private Task FireHookAsync(string checkpoint, CancellationToken cancellationToken) =>
        MidTransactionHook?.Invoke(checkpoint, cancellationToken) ?? Task.CompletedTask;

    /// <inheritdoc />
    public async ValueTask<ResourceMovePreview> PreviewIntegrationMoveAsync(
        IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        // House pattern (IntegrationManagementService.UpdateIntegrationAsync): resolve the actor before
        // touching the repository, so an unauthenticated caller cannot use the not-found/unauthorized
        // exception distinction as an existence oracle for ids it was never entitled to probe.
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var integration = await _integrationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Integration {id.Value} not found.");
        await AuthorizeAsync(actor, ActionConstants.IntegrationUpdate, integration.GetIdentity(), cancellationToken).ConfigureAwait(false);
        var validation = await ValidateIntegrationMoveAsync(integration, targetResourceContextId, cancellationToken).ConfigureAwait(false);
        return validation.Preview;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceMovePreview> PreviewConnectionMoveAsync(
        ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        // Same house pattern as PreviewIntegrationMoveAsync: resolve the actor before touching the
        // repository, so an unauthenticated caller cannot use the not-found/unauthorized exception
        // distinction as an existence oracle for ids it was never entitled to probe.
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Connection {id.Value} not found.");
        await AuthorizeAsync(actor, ActionConstants.ConnectionUpdate, connection.GetIdentity(), cancellationToken).ConfigureAwait(false);
        return await ValidateConnectionMoveAsync(connection, targetResourceContextId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceMovePreview> PreviewScheduleMoveAsync(
        ScheduleId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        // Same house pattern as PreviewIntegrationMoveAsync/PreviewConnectionMoveAsync: resolve the actor
        // before touching the repository, so an unauthenticated caller cannot use the not-found/unauthorized
        // exception distinction as an existence oracle for ids it was never entitled to probe.
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        // Unlike Integration/Connection moves, Schedule authorization is instance-scoped (§9.3): there is
        // no per-Schedule governed identity to authorize against here -- this mirrors how `GET
        // api/schedules` is already gated, via schedule.manage against the Schedule resource type at
        // Instance scope, never resource_context.manage. Because this check needs nothing from the loaded
        // row, it runs BEFORE the repository load (house pattern:
        // OperationalTeamManagementService.RequireTeamManageAsync /
        // AttentionContactGroupManagementService's equivalent) -- unlike Integration/Connection, whose
        // authorization is against the loaded entity's governed identity and so cannot run first. This
        // keeps an unauthorized caller from using not-found vs. unauthorized as a schedule-existence oracle.
        await AuthorizeScheduleAsync(actor, cancellationToken).ConfigureAwait(false);
        var schedule = await _scheduleRepository.GetDefinitionAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Schedule {id.Value} not found.");
        return await ValidateScheduleMoveAsync(schedule, targetResourceContextId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What a move attempt decided: the preview, and the refusal to record after the unit when the preview blocked it.</summary>
    private sealed record MoveAttempt(ResourceMovePreview Preview, RejectedMove? Rejected);

    private sealed record RejectedMove(string TargetType, string ResourceId, string Name, string SourceContextId, string TargetContextId, IReadOnlyList<string> BlockerCodes);

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5) for the whole ownership change: the integration, every attached schedule and
    /// the pair of rows commit together or not at all. Inside the attempt, in this order: the ownership gate (before any
    /// ownership-sensitive read), the integration row (locked to the end of the transaction), <c>integration.update</c> authorized
    /// against the row as locked (so a move that committed while this one waited is what the decision reads), the identical
    /// validator preview uses, then the change. The attached schedules moved, the schedules the operator saw as an effect, and the
    /// ids recorded are the one set the validator read inside this transaction.</para>
    /// <para>A blocked move changes nothing and declares itself a no-op: the unit cannot stage a refusal, so the refusal is
    /// recorded after it as a best-effort attempt of the same principal (<c>integration.ownership.move_rejected</c>, write class
    /// W3). The committed pair is <c>moved_out</c> (primary, related to the source context) and <c>moved_in</c> (related to the
    /// target), sharing the operation's event id as their correlation.</para>
    /// </remarks>
    public async ValueTask<ResourceMovePreview> MoveIntegrationAsync(
        IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;

        var result = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationMovedOut, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Gate first, before ANY ownership-sensitive read (product decision P1).
                await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                // Defense in depth on the row we will mutate (§8.4 step 1).
                var integration = await _integrationRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Integration {id.Value} not found.");

                await AuthorizeAsync(actor, ActionConstants.IntegrationUpdate, integration.GetIdentity(), ct).ConfigureAwait(false);

                // The identical validator preview uses (§8.2) -- now against state re-read inside the gate.
                var validation = await ValidateIntegrationMoveAsync(integration, targetResourceContextId, ct).ConfigureAwait(false);
                var preview = validation.Preview;

                await FireHookAsync("integration:validated", ct).ConfigureAwait(false);

                var sourceContextId = integration.ResourceContextId.Value;
                if (!preview.CanMove)
                {
                    transaction.DeclareNoOp();
                    return new MoveAttempt(preview, new RejectedMove(
                        AuditTargetTypes.Integration, id.Value, integration.DisplayName, sourceContextId, targetResourceContextId.Value,
                        preview.Blockers.Select(b => b.Code).ToList()));
                }

                // The validator's authoritative set -- never a second query. The schedules_move_with_integration effect the
                // operator saw, the rows actually mutated, and this audit payload all describe the same schedules from one read
                // inside one transaction.
                var movedScheduleIds = validation.AttachedSchedules.Select(s => s.Id.Value).ToList();
                await StageMovePairAsync(
                    transaction, AuditTargetTypes.Integration, id.Value, integration.DisplayName, sourceContextId, targetResourceContextId.Value, movedScheduleIds, ct)
                    .ConfigureAwait(false);

                await _integrationRepository.SetResourceContextIdAsync(id, targetResourceContextId, ct).ConfigureAwait(false);
                foreach (var schedule in validation.AttachedSchedules)
                    await _scheduleRepository.SetResourceContextIdAsync(schedule.Id, targetResourceContextId, ct).ConfigureAwait(false);

                return new MoveAttempt(preview, null);
            },
            async (committed, ct) => new MoveAttempt(await ReconstructMoveAsync(committed, AuditTargetTypes.Integration, id.Value, ct).ConfigureAwait(false), null),
            cancellationToken).ConfigureAwait(false);

        return await FinishAsync(result).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5): gate, the connection row (locked), <c>connection.update</c> authorized
    /// against the row as locked, the shared validator, then the change and the pair of rows; a blocked move is recorded after the
    /// unit as <c>connection.ownership.move_rejected</c>. See <see cref="MoveIntegrationAsync"/>. The connection's secrets are not
    /// touched: moving contexts never moves or copies secret material.</para>
    /// </remarks>
    public async ValueTask<ResourceMovePreview> MoveConnectionAsync(
        ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;

        var result = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ConnectionMovedOut, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Gate first, before ANY ownership-sensitive read (product decision P1).
                await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                // Defense in depth on the row we will mutate (§8.4 step 1).
                var connection = await _connectionRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Connection {id.Value} not found.");

                await AuthorizeAsync(actor, ActionConstants.ConnectionUpdate, connection.GetIdentity(), ct).ConfigureAwait(false);

                // The identical validator preview uses (§8.2) -- now against state re-read inside the gate.
                var preview = await ValidateConnectionMoveAsync(connection, targetResourceContextId, ct).ConfigureAwait(false);

                await FireHookAsync("connection:validated", ct).ConfigureAwait(false);

                var sourceContextId = connection.ResourceContextId.Value;
                if (!preview.CanMove)
                {
                    transaction.DeclareNoOp();
                    return new MoveAttempt(preview, new RejectedMove(
                        AuditTargetTypes.Connection, id.Value, connection.Name, sourceContextId, targetResourceContextId.Value,
                        preview.Blockers.Select(b => b.Code).ToList()));
                }

                await StageMovePairAsync(
                    transaction, AuditTargetTypes.Connection, id.Value, connection.Name, sourceContextId, targetResourceContextId.Value, null, ct)
                    .ConfigureAwait(false);
                await _connectionRepository.SetResourceContextIdAsync(id, targetResourceContextId, ct).ConfigureAwait(false);
                return new MoveAttempt(preview, null);
            },
            async (committed, ct) => new MoveAttempt(await ReconstructMoveAsync(committed, AuditTargetTypes.Connection, id.Value, ct).ConfigureAwait(false), null),
            cancellationToken).ConfigureAwait(false);

        return await FinishAsync(result).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5): gate, then the instance-scoped <c>schedule.manage</c> check (before the
    /// read, as the preview does: it needs nothing from the row, and running it after would let the exception type reveal whether
    /// a schedule exists to a caller who was never entitled to probe it), the schedule row (locked), the shared validator, then the
    /// change and the pair of rows; a blocked move is recorded after the unit as <c>schedule.ownership.move_rejected</c>. An
    /// integration-attached schedule never moves on its own. See <see cref="MoveIntegrationAsync"/>.</para>
    /// </remarks>
    public async ValueTask<ResourceMovePreview> MoveScheduleAsync(
        ScheduleId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;

        var result = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ScheduleMovedOut, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Gate first, before ANY ownership-sensitive read (product decision P1).
                await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                // Authorized before the locked read -- mirrors PreviewScheduleMoveAsync's existing order, and for the same
                // reason: AuthorizeScheduleAsync (§9.3) needs only the actor, not the loaded entity's identity, so running it
                // after the read would let exception type leak whether a Schedule id exists (the oracle CTX-3B Plan 1's final
                // review fixed; see ScheduleMovePreviewTests.Not_authorized_takes_priority_over_unknown_schedule_id). Do not
                // reorder this to "match" the Integration and Connection moves: theirs is forced by a real data dependency.
                await AuthorizeScheduleAsync(actor, ct).ConfigureAwait(false);

                // Defense in depth on the row we will mutate (§8.4 step 1).
                var schedule = await _scheduleRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Schedule {id.Value} not found.");

                // The identical validator preview uses (§8.2) -- now against state re-read inside the gate.
                var preview = await ValidateScheduleMoveAsync(schedule, targetResourceContextId, ct).ConfigureAwait(false);

                await FireHookAsync("schedule:validated", ct).ConfigureAwait(false);

                // schedule.ResourceContextId is non-null here: ValidateScheduleMoveAsync throws were it ever null.
                var sourceContextId = schedule.ResourceContextId!.Value;
                if (!preview.CanMove)
                {
                    transaction.DeclareNoOp();
                    return new MoveAttempt(preview, new RejectedMove(
                        AuditTargetTypes.Schedule, id.Value, schedule.Name, sourceContextId, targetResourceContextId.Value,
                        preview.Blockers.Select(b => b.Code).ToList()));
                }

                await StageMovePairAsync(
                    transaction, AuditTargetTypes.Schedule, id.Value, schedule.Name, sourceContextId, targetResourceContextId.Value, null, ct)
                    .ConfigureAwait(false);
                await _scheduleRepository.SetResourceContextIdAsync(id, targetResourceContextId, ct).ConfigureAwait(false);
                return new MoveAttempt(preview, null);
            },
            async (committed, ct) => new MoveAttempt(await ReconstructMoveAsync(committed, AuditTargetTypes.Schedule, id.Value, ct).ConfigureAwait(false), null),
            cancellationToken).ConfigureAwait(false);

        return await FinishAsync(result).ConfigureAwait(false);
    }

    /// <summary>
    /// Stages the committed pair: <c>moved_out</c> (the unit's primary row, related to the source context) and <c>moved_in</c>
    /// (related to the target), with the operation's event id as their shared correlation. The ids of the attached schedules
    /// (integration moves only) are the validator's, counted exactly and listed up to a bound.
    /// </summary>
    private static async ValueTask StageMovePairAsync(
        IAuditedTransaction transaction,
        string targetType,
        string resourceId,
        string name,
        string sourceContextId,
        string targetContextId,
        IReadOnlyList<string>? movedScheduleIds,
        CancellationToken cancellationToken)
    {
        await transaction.StageAsync(
            AuditEvents.OwnershipMovedOut(targetType, resourceId, name, sourceContextId, targetContextId, movedScheduleIds, transaction.OperationEventId),
            cancellationToken).ConfigureAwait(false);
        await transaction.StageAsync(
            AuditEvents.OwnershipMovedIn(targetType, resourceId, name, sourceContextId, targetContextId, movedScheduleIds, transaction.OperationEventId),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a blocked move after the unit, as a best-effort attempt of the same principal (a failure to write it is logged by
    /// the writer and never changes the answer), and returns the preview the operator is shown.
    /// </summary>
    private async ValueTask<ResourceMovePreview> FinishAsync(MoveAttempt result)
    {
        if (result.Rejected is { } rejected && _auditEvents is not null)
        {
            await _auditEvents.TryWriteAsync(
                AuditEvents.OwnershipMoveRejected(
                    rejected.TargetType, rejected.ResourceId, rejected.Name, rejected.SourceContextId, rejected.TargetContextId, rejected.BlockerCodes),
                AuditActor.CurrentPrincipal,
                CancellationToken.None).ConfigureAwait(false);
        }

        return result.Preview;
    }

    /// <summary>
    /// Builds the result of a move whose commit was verified after a lost acknowledgement, from the committed primary row (the
    /// contexts, and for an integration the ids of the schedules that moved with it) and, for a connection, the integrations that
    /// use it now. The preview of a move that has happened cannot be recomputed: the resource is already in the target.
    /// </summary>
    private async ValueTask<ResourceMovePreview> ReconstructMoveAsync(
        AuditCommittedOperation committed, string targetType, string resourceId, CancellationToken cancellationToken)
    {
        var sourceId = committed.GetField("sourceContextId") ?? throw new InvalidOperationException("The committed move names no source context.");
        var targetId = committed.GetField("targetContextId") ?? throw new InvalidOperationException("The committed move names no target context.");
        var sourceRef = await ResolveRefAsync(new ResourceContextId(sourceId), cancellationToken).ConfigureAwait(false);
        var targetRef = await ResolveRefAsync(new ResourceContextId(targetId), cancellationToken).ConfigureAwait(false);

        var effects = new List<ResourceMoveEffect>();
        if (targetType == AuditTargetTypes.Integration)
        {
            // The count is exact; the listed ids are bounded (the first 500).
            var moved = committed.GetStringList("scheduleIds");
            var movedCount = committed.GetCount("schedulesMoved") ?? moved.Count;
            if (movedCount > 0)
            {
                effects.Add(new ResourceMoveEffect(
                    ResourceMoveEffectCodes.SchedulesMoveWithIntegration,
                    $"{movedCount} schedule(s) move with this integration.",
                    ResourceMoveResourceTypes.Schedule, moved));
            }
        }
        else if (targetType == AuditTargetTypes.Connection)
        {
            var usedBy = new List<string>();
            var bindings = await _bindingRepository.GetByConnectionIdAsync(new ConnectionId(resourceId), cancellationToken).ConfigureAwait(false);
            foreach (var integrationId in bindings.Where(b => b.IntegrationId is not null).Select(b => b.IntegrationId!).DistinctBy(i => i.Value).OrderBy(i => i.Value, StringComparer.Ordinal))
            {
                if (await _integrationRepository.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false) is not null)
                    usedBy.Add(integrationId.Value);
            }

            if (usedBy.Count > 0)
            {
                effects.Add(new ResourceMoveEffect(
                    ResourceMoveEffectCodes.UsedByIntegration, $"Used by {usedBy.Count} integration(s).", ResourceMoveResourceTypes.Integration, usedBy));
            }
        }

        var type = targetType switch
        {
            AuditTargetTypes.Integration => ResourceMoveResourceTypes.Integration,
            AuditTargetTypes.Connection => ResourceMoveResourceTypes.Connection,
            _ => ResourceMoveResourceTypes.Schedule,
        };
        return BuildPreview(type, resourceId, sourceRef, targetRef, [], effects);
    }

    /// <summary>
    /// The Integration validator's full result: the operator-facing preview, plus the authoritative
    /// attached-Schedule set it was computed from. <see cref="AttachedSchedules"/> is authoritative only
    /// when <c>Preview.CanMove</c> is <see langword="true"/> -- on the common-blocker early return
    /// (<c>target_not_found</c>/<c>target_archived</c>/<c>already_in_target</c>) it is always <c>[]</c>,
    /// even when the Integration does have Schedules, because that branch returns before the Schedule
    /// read runs (Plan 2 must not mutate when <c>CanMove</c> is false). Plan 2 mutates exactly this set
    /// and Plan 4 audits exactly this set when <c>CanMove</c> is true, so all three describe the same
    /// schedules from one read inside one transaction. Private: never part of the API or client contract.
    /// </summary>
    private sealed record IntegrationMoveValidation(
        ResourceMovePreview Preview,
        IReadOnlyList<ScheduleDefinition> AttachedSchedules);

    /// <summary>
    /// Shared validator for Integration moves (§8.4). Called by preview here and, unchanged, by the
    /// gated transaction in Plan 2 -- this is the mechanism that prevents preview/move drift (§8.2).
    /// </summary>
    private async ValueTask<IntegrationMoveValidation> ValidateIntegrationMoveAsync(
        DomainIntegration integration, ResourceContextId targetId, CancellationToken cancellationToken)
    {
        var sourceId = integration.ResourceContextId;
        var sourceRef = await ResolveRefAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var target = await _resourceContexts.GetAsync(targetId, cancellationToken).ConfigureAwait(false);

        if (!TryResolveMovableTarget(
                sourceId, targetId, target, "The integration is already owned by the requested context.",
                out var resolvedTarget, out var targetRef, out var blockers))
        {
            return new(BuildPreview(ResourceMoveResourceTypes.Integration, integration.Id.Value, sourceRef, targetRef, blockers, []), []);
        }

        var effects = new List<ResourceMoveEffect>();

        // Deterministic ordering (Global Constraints): the repository methods below have no ORDER BY, so
        // SQL Server row order is unspecified. Sorting here is what makes the blocker/effect lists stable
        // for the API response, the UI dialog, and the audit payload.
        var bindings = (await _bindingRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(b => b.Id.Value, StringComparer.Ordinal)
            .ToList();

        // De-duplicated by Connection id (review fix round 1, Important 2, mirror of the Connection
        // validator's fix): an Integration bound to the SAME Connection across two slots must produce one
        // blocker for that Connection, not one per binding. Distinct Connections still each keep their own
        // blocker -- see Multiple_unreachable_connections_each_produce_their_own_blocker.
        var blockedConnectionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding.ConnectionId is not { } connectionId)
                continue; // capability binding: no Connection to check.

            var connection = await _connectionRepository.GetByIdAsync(connectionId, cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                // Deliberately NOT de-duplicated per Connection (unlike the unreachable blocker below):
                // this effect names the binding (`binding.SlotName`/`binding.Id`), not a resource, so two
                // bindings that happen to reference the same missing Connection id are two distinct dangling
                // edges, each with its own slot name -- collapsing them would hide one of the two broken
                // slots from the operator. Do not "harmonize" this with the resource-level de-duplication.
                effects.Add(new ResourceMoveEffect(
                    ResourceMoveEffectCodes.BindingReferencesMissingConnection,
                    $"Binding for slot '{binding.SlotName}' references a missing connection; the move does not make this worse.",
                    ResourceMoveResourceTypes.Binding, [binding.Id.Value]));
                continue;
            }

            if (!ResourceContextReferenceRule.CanReference(targetId, connection.ResourceContextId)
                && blockedConnectionIds.Add(connection.Id.Value))
            {
                // resolvedTarget is compiler-proven non-null here (TryResolveMovableTarget's
                // [NotNullWhen(true)] contract, checked above) -- no null-forgiving operator needed.
                blockers.Add(new ResourceMoveBlocker(
                    ResourceMoveBlockerCodes.BindingConnectionUnreachable,
                    $"Connection '{connection.Name}' is owned by '{connection.ResourceContextId.Value}' and would become unreachable if this integration moved to '{resolvedTarget.Name}'.",
                    ResourceMoveResourceTypes.Connection, connection.Id.Value, connection.Name, connection.ResourceContextId.Value));
            }
        }

        // The authoritative attached-Schedule set. Read exactly once; returned to the caller so Plan 2's
        // mutation and Plan 4's audit describe the same schedules this preview described.
        var schedules = (await _scheduleRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
            .ToList();

        // NOTE (verified at review time): SqlScheduleRepository.ToDefinition coalesces a null/whitespace
        // ResourceContextId column to Shared, so `s.ResourceContextId` is never null coming out of the
        // repository. The null test below is retained only as a defensive guard against a future mapper
        // change -- it is not the mechanism that catches legacy NULL rows. Legacy NULL rows present as
        // Shared and therefore land in the `!= sourceId` branch. See this plan's "Legacy schedule
        // ownership drift" note and Plan 2's preflight (product decision P3).
        var inconsistent = schedules
            .Where(s => s.ResourceContextId is null || !string.Equals(s.ResourceContextId.Value, sourceId.Value, StringComparison.Ordinal))
            .ToList();
        if (inconsistent.Count > 0)
        {
            foreach (var s in inconsistent)
            {
                blockers.Add(new ResourceMoveBlocker(
                    ResourceMoveBlockerCodes.ScheduleContextInconsistent,
                    $"Schedule '{s.Name}' does not currently match this integration's context and must be corrected before it can move.",
                    ResourceMoveResourceTypes.Schedule, s.Id.Value, s.Name, s.ResourceContextId?.Value));
            }
        }
        else if (schedules.Count > 0)
        {
            effects.Add(new ResourceMoveEffect(
                ResourceMoveEffectCodes.SchedulesMoveWithIntegration,
                $"{schedules.Count} schedule(s) move with this integration.",
                ResourceMoveResourceTypes.Schedule, schedules.Select(s => s.Id.Value).ToList()));
        }

        return new(BuildPreview(ResourceMoveResourceTypes.Integration, integration.Id.Value, sourceRef, targetRef, blockers, effects), schedules);
    }

    /// <summary>
    /// Shared validator for Connection moves (§8.5). Frozen signature: Plan 2 calls this unchanged inside
    /// a locked transaction, so preview and move cannot drift (§8.2).
    /// </summary>
    /// <remarks>
    /// Direction note (design gate §8.5 step 3): for a Connection move, the bound Integration is the
    /// owner and stays put while the Connection becomes owned by the target, so the reference check is
    /// <c>CanReference(integration.ResourceContextId, targetId)</c> -- the opposite argument order from
    /// the Integration validator's <c>CanReference(targetId, connection.ResourceContextId)</c>, because in
    /// each case the argument order reflects whichever resource is the one actually moving.
    /// </remarks>
    private async ValueTask<ResourceMovePreview> ValidateConnectionMoveAsync(
        Connection connection, ResourceContextId targetId, CancellationToken cancellationToken)
    {
        var sourceId = connection.ResourceContextId;
        var sourceRef = await ResolveRefAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var target = await _resourceContexts.GetAsync(targetId, cancellationToken).ConfigureAwait(false);

        if (!TryResolveMovableTarget(
                sourceId, targetId, target, "The connection is already owned by the requested context.",
                out _, out var targetRef, out var blockers))
        {
            return BuildPreview(ResourceMoveResourceTypes.Connection, connection.Id.Value, sourceRef, targetRef, blockers, []);
        }

        var effects = new List<ResourceMoveEffect>();

        // Deterministic ordering (Global Constraints): GetByConnectionIdAsync has no ORDER BY.
        var bindings = (await _bindingRepository.GetByConnectionIdAsync(connection.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(b => b.Id.Value, StringComparer.Ordinal)
            .ToList();

        // De-duplicated by Integration id (review fix round 1, Important 2/Minor 3): a Connection bound
        // into two slots of the SAME Integration is an ordinary configuration, not two distinct blast-radius
        // entries -- both the "Used by" count and the unreachable blocker must describe each distinct
        // Integration once, never once per binding. A binding whose Integration row is missing is never
        // counted as "used by" at all -- the dangling edge is already surfaced via the advisory effect below,
        // and naming a phantom Integration in the blast-radius payload would mislead the operator.
        var usedByIntegrationIds = new List<string>();
        var seenUsedByIntegrationIds = new HashSet<string>(StringComparer.Ordinal);
        var blockedIntegrationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding.IntegrationId is { } integrationId)
            {
                var integration = await _integrationRepository.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false);
                if (integration is null)
                {
                    // Mirror of the Integration validator's missing-connection advisory: surface the
                    // dangling edge, never silently skip it. The move cannot make this worse.
                    // Deliberately NOT de-duplicated per Integration (unlike the unreachable blocker below):
                    // this effect names the binding, not a resource, so two bindings referencing the same
                    // missing Integration id are two distinct dangling slots and must both be reported. Do
                    // not "harmonize" this with the resource-level de-duplication.
                    effects.Add(new ResourceMoveEffect(
                        ResourceMoveEffectCodes.BindingReferencesMissingIntegration,
                        $"Binding for slot '{binding.SlotName}' references a missing integration; the move does not make this worse.",
                        ResourceMoveResourceTypes.Binding, [binding.Id.Value]));
                    continue;
                }

                if (seenUsedByIntegrationIds.Add(integrationId.Value))
                    usedByIntegrationIds.Add(integrationId.Value);

                if (!ResourceContextReferenceRule.CanReference(integration.ResourceContextId, targetId)
                    && blockedIntegrationIds.Add(integrationId.Value))
                {
                    blockers.Add(new ResourceMoveBlocker(
                        ResourceMoveBlockerCodes.BindingIntegrationUnreachable,
                        $"Integration '{integration.DisplayName}' (in '{integration.ResourceContextId.Value}') would no longer be able to reference this connection.",
                        ResourceMoveResourceTypes.Integration, integration.Id.Value, integration.DisplayName, integration.ResourceContextId.Value));
                }
            }
            else if (!ResourceContextIds.IsShared(targetId))
            {
                blockers.Add(new ResourceMoveBlocker(
                    ResourceMoveBlockerCodes.UnownedBindingRequiresShared,
                    "A platform-level binding uses this connection; it must stay in Shared.",
                    ResourceMoveResourceTypes.Binding, binding.Id.Value, null, null));
            }
        }

        if (usedByIntegrationIds.Count > 0)
        {
            effects.Add(new ResourceMoveEffect(
                ResourceMoveEffectCodes.UsedByIntegration,
                $"Used by {usedByIntegrationIds.Count} integration(s).",
                ResourceMoveResourceTypes.Integration, usedByIntegrationIds));
        }

        return BuildPreview(ResourceMoveResourceTypes.Connection, connection.Id.Value, sourceRef, targetRef, blockers, effects);
    }

    /// <summary>
    /// Shared validator for Schedule moves (§8.6). Frozen signature (this task's interface contract for
    /// Plan 2): Plan 2 calls this unchanged inside a locked transaction, so preview and move cannot drift
    /// (§8.2).
    /// </summary>
    /// <remarks>
    /// The attachment check runs <em>before</em> the target checks, deliberately: an Integration-attached
    /// Schedule cannot move independently at all (§8.6), so its preview reports only the attachment
    /// blocker even when the requested target does not exist or is archived -- the operator's actionable
    /// next step is always "move the integration instead," never "also fix the target." A standalone
    /// (legacy) Schedule falls through to the same common target/already-in-target checks every other
    /// family uses.
    /// </remarks>
    private async ValueTask<ResourceMovePreview> ValidateScheduleMoveAsync(
        ScheduleDefinition schedule, ResourceContextId targetId, CancellationToken cancellationToken)
    {
        var sourceId = schedule.ResourceContextId
            ?? throw new InvalidOperationException($"Schedule {schedule.Id.Value} has no resolved ResourceContext.");
        var sourceRef = await ResolveRefAsync(sourceId, cancellationToken).ConfigureAwait(false);

        if (schedule.IntegrationId is { } integrationId)
        {
            var integration = await _integrationRepository.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false);
            var attachedBlockers = new List<ResourceMoveBlocker>
            {
                new(ResourceMoveBlockerCodes.ScheduleAttachedToIntegration,
                    "Move the integration instead; its schedules move with it.",
                    ResourceMoveResourceTypes.Integration, integrationId.Value, integration?.DisplayName, integration?.ResourceContextId.Value)
            };
            // Still resolve the real target for display (review fix round 1, Important 1): the attachment
            // blocker fires regardless of whether the target exists, but `preview.Target` is part of the
            // API/UI contract and must show the actual destination whenever one was resolvable -- the
            // synthetic ref is only a fallback for a genuinely nonexistent target, not a shortcut taken
            // just because this branch already knows the move is blocked.
            var attachedTarget = await _resourceContexts.GetAsync(targetId, cancellationToken).ConfigureAwait(false);
            // Synthetic fallback, only reached when the target ResourceContext does not exist: `Slug` is
            // the raw 32-hex id, not a real slug. CanMove is always false here (attachment blocker), so
            // nothing is submittable -- but Plan 5's move dialog builds routes from Target.Slug, so this
            // value must never be used to construct a link. Do not change this shape; see IResourceContextService.
            var attachedTargetRef = attachedTarget?.ToRef() ?? new ResourceContextRef(targetId.Value, targetId.Value, targetId.Value);
            return BuildPreview(ResourceMoveResourceTypes.Schedule, schedule.Id.Value, sourceRef, attachedTargetRef, attachedBlockers, []);
        }

        var target = await _resourceContexts.GetAsync(targetId, cancellationToken).ConfigureAwait(false);
        TryResolveMovableTarget(
            sourceId, targetId, target, "The schedule is already owned by the requested context.",
            out _, out var targetRef, out var blockers);
        return BuildPreview(ResourceMoveResourceTypes.Schedule, schedule.Id.Value, sourceRef, targetRef, blockers, []);
    }

    /// <summary>
    /// Resolves the current actor, failing fast if there is none. Shared by every <c>Preview*</c>/<c>Move*</c>
    /// method (Tasks 5 and 6 reuse this for the Connection and Schedule families): callers must resolve the
    /// actor <em>before</em> loading the target resource, matching the house pattern
    /// (<c>IntegrationManagementService.UpdateIntegrationAsync</c>) -- otherwise an unauthenticated caller
    /// could distinguish "not found" from "unauthorized" purely from exception type, which leaks resource
    /// existence to callers who were never authenticated at all.
    /// </summary>
    private async ValueTask<IActor> RequireActorAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        return actor ?? throw new UnauthorizedAccessException("No authenticated actor.");
    }

    /// <summary>
    /// Evaluates whether <paramref name="actor"/> (already resolved via <see cref="RequireActorAsync"/>)
    /// holds <paramref name="action"/> against <paramref name="identity"/>. Shared by every move family.
    /// </summary>
    private async ValueTask AuthorizeAsync(IActor actor, string action, IGovernedResourceIdentity identity, CancellationToken cancellationToken)
    {
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(action, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException($"Not authorized to move this resource ({action}).");
    }

    /// <summary>
    /// Schedule-specific authorization (§9.3): unlike <see cref="AuthorizeAsync"/>, this evaluates
    /// <see cref="ActionConstants.ScheduleManage"/> against an instance-scoped
    /// <see cref="ResourceDescriptor"/> (<see cref="ResourceTypeConstants.Schedule"/> /
    /// <see cref="ScopeKeyConstants.Instance"/>) rather than a per-resource governed identity, because
    /// that is how <c>GET api/schedules</c> is already gated. Never <c>resource_context.manage</c>.
    /// </summary>
    private async ValueTask AuthorizeScheduleAsync(IActor actor, CancellationToken cancellationToken)
    {
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ScheduleManage, new ResourceDescriptor(ResourceTypeConstants.Schedule, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to move schedules.");
    }

    private async ValueTask<ResourceContextRef> ResolveRefAsync(ResourceContextId id, CancellationToken cancellationToken)
    {
        var ctx = await _resourceContexts.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ctx?.ToRef() ?? new ResourceContextRef(id.Value, id.Value, id.Value);
    }

    /// <summary>
    /// Evaluates the three common blockers shared by every move family (§8.3): target not found, target
    /// archived, already in target. These are mutually exclusive, so at most one blocker is ever produced.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when one of the three blockers applies -- <paramref name="blockers"/> then
    /// contains it (and <paramref name="resolvedTarget"/> is <see langword="null"/>), and the caller
    /// should return immediately using <paramref name="targetRef"/>. <see langword="true"/> when the
    /// target is safe to move into -- <paramref name="blockers"/> is then empty and
    /// <paramref name="resolvedTarget"/> is guaranteed non-null via <see cref="NotNullWhenAttribute"/>, so
    /// callers that need <c>resolvedTarget.Name</c> (the Integration validator's unreachable-connection
    /// message) get that non-nullness proven by the compiler rather than asserted with a null-forgiving
    /// operator -- a future change to this method's short-circuit contract becomes a build break here,
    /// not a silent <see cref="NullReferenceException"/> at the call site.
    /// </returns>
    /// <remarks>
    /// <paramref name="alreadyInTargetMessage"/> is family-specific (it must still name the right resource
    /// noun -- "The integration/connection/schedule is already owned...") so this helper cannot flatten
    /// the three families' messages to one generic string. <paramref name="targetRef"/> is always the
    /// resolved <c>target.ToRef()</c> except in the target-not-found case, where it is the synthetic
    /// fallback -- so callers never repeat the <see cref="IResourceContextService.GetAsync"/> lookup.
    /// </remarks>
    private static bool TryResolveMovableTarget(
        ResourceContextId sourceId, ResourceContextId targetId, ResourceContext? target, string alreadyInTargetMessage,
        [NotNullWhen(true)] out ResourceContext? resolvedTarget,
        out ResourceContextRef targetRef,
        out List<ResourceMoveBlocker> blockers)
    {
        blockers = [];

        if (target is null)
        {
            blockers.Add(new ResourceMoveBlocker(
                ResourceMoveBlockerCodes.TargetNotFound, $"ResourceContext '{targetId.Value}' does not exist.",
                null, null, null, null));
            // Synthetic fallback, only reached when the target ResourceContext does not exist: `Slug` is
            // the raw 32-hex id, not a real slug. CanMove is false here, so nothing is submittable -- but
            // Plan 5's move dialog builds routes from Target.Slug, so this value must never be used to
            // construct a link. Do not change this shape.
            targetRef = new ResourceContextRef(targetId.Value, targetId.Value, targetId.Value);
            resolvedTarget = null;
            return false;
        }
        if (!target.IsActive)
        {
            blockers.Add(new ResourceMoveBlocker(
                ResourceMoveBlockerCodes.TargetArchived, $"'{target.Name}' is archived and cannot receive new ownership.",
                ResourceMoveResourceTypes.ResourceContext, target.Id.Value, target.Name, target.Id.Value));
            targetRef = target.ToRef();
            resolvedTarget = null;
            return false;
        }
        if (string.Equals(sourceId.Value, targetId.Value, StringComparison.Ordinal))
        {
            blockers.Add(new ResourceMoveBlocker(ResourceMoveBlockerCodes.AlreadyInTarget, alreadyInTargetMessage, null, null, null, null));
            targetRef = target.ToRef();
            resolvedTarget = null;
            return false;
        }

        targetRef = target.ToRef();
        resolvedTarget = target;
        return true;
    }

    /// <summary>
    /// Assembles the final <see cref="ResourceMovePreview"/> for any move family. Replaces the three
    /// near-identical <c>Build*Preview</c> helpers this plan started with (one per family) -- extracted
    /// once a third copy (Schedule) would have made it three, per review guidance that two near-duplicates
    /// are acceptable but three are not.
    /// </summary>
    private static ResourceMovePreview BuildPreview(
        string resourceType, string resourceId, ResourceContextRef source, ResourceContextRef target,
        List<ResourceMoveBlocker> blockers, List<ResourceMoveEffect> effects) =>
        new(resourceType, resourceId, source, target, blockers.Count == 0, blockers, effects);
}
