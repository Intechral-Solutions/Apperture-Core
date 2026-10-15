using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Integration management workflow with authorization enforcement. Epic 49. Every mutation is one audited unit: the hard-delete
/// (AUD-1.4 slice 3, work package 4b: the integration, its setting overrides, the secrets they owned and the audit rows commit
/// together or not at all) and create, update and the state transitions (slice 4).
/// </summary>
public sealed class IntegrationManagementService : IIntegrationManagementService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IResourceContextService _resourceContexts;
    private readonly AppertureDbContext _db;
    private readonly IResourceOwnershipMutationGate _ownershipGate;
    private readonly IntegrationSettingSecretClassifier? _settingSecretClassifier;
    private readonly IntegrationSettingSecretRetirement? _settingSecretRetirement;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public IntegrationManagementService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrationRepository,
        IResourceContextService resourceContexts,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate,
        IntegrationSettingSecretClassifier? settingSecretClassifier = null,
        IntegrationSettingSecretRetirement? settingSecretRetirement = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _integrationRepository = integrationRepository;
        _resourceContexts = resourceContexts;
        _db = db;
        _ownershipGate = ownershipGate;
        // Optional so a service that only reads integrations needs none of them; every mutation refuses to run without the unit
        // rather than change anything unaudited, and the delete also needs the two settings-secret collaborators.
        _settingSecretClassifier = settingSecretClassifier;
        _settingSecretRetirement = settingSecretRetirement;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DomainIntegration>> ListIntegrationsAsync(CancellationToken cancellationToken = default) =>
        ListIntegrationsAsync(resourceContextId: null, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DomainIntegration>> ListIntegrationsAsync(
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default) =>
        ListIntegrationsAsync(resourceContextId, moduleId: null, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DomainIntegration>> ListIntegrationsAsync(
        ResourceContextId? resourceContextId,
        string? moduleId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.IntegrationRead, new ResourceDescriptor(ResourceTypeConstants.Integration, ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to list integrations.");

        var scopeKey = ScopeKeyConstants.Instance;
        return await _integrationRepository.GetVisibleAsync(scopeKey, resourceContextId, moduleId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<DomainIntegration?> GetIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var integration = await _integrationRepository.GetByIdAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var identity = integration.GetIdentity();
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.IntegrationRead, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to view this integration.");

        return integration;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 4). Inside the attempt, in this order: the ownership gate (a new Active
    /// integration is an archive-blocking resource entering a context, so it must not interleave with an Archive evaluating that
    /// context), the owning context (a supplied context must exist and be Active; none means the reserved Shared context), the
    /// integration, and <c>integration.created</c>. The identity is chosen before the unit, so a rerun after a confirmed rollback
    /// creates the same integration, and a verified commit after a lost acknowledgement is reconstructed from the committed row by
    /// that id. A settings dictionary is recorded only as "provided", never by key or value.</para>
    /// </remarks>
    public async ValueTask<DomainIntegration> CreateIntegrationAsync(CreateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var unit = _auditedUnitOfWork
            ?? throw new AuditInvariantException("Integration creation requires the audited unit of work.");

        var scopeKey = request.ScopeKey ?? ScopeKeyConstants.Instance;
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.IntegrationCreate, new ResourceDescriptor(ResourceTypeConstants.Integration, scopeKey)),
                null),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to create integrations.");

        var id = new IntegrationId(Guid.NewGuid().ToString("N")[..24]);
        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationCreated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Gate first: closes the create-vs-archive race (CTX-3B follow-on).
                await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                // Ownership defaults to the reserved Shared ResourceContext when the caller supplies none, so existing create
                // clients stay compatible. A supplied context must exist and be Active.
                var owningContext = await _resourceContexts.ResolveForNewOwnershipAsync(request.ResourceContextId, ct).ConfigureAwait(false);

                var now = DateTimeOffset.UtcNow;
                var integration = new DomainIntegration(id, request.DisplayName, request.ModuleId, request.ScopeKey, IntegrationState.Active, request.Settings, now, now)
                {
                    ResourceContextId = owningContext.Id
                };

                await transaction.StageAsync(
                    AuditEvents.IntegrationCreated(
                        id.Value, integration.DisplayName, integration.ModuleId, request.Settings is { Count: > 0 }, owningContext.Id.Value),
                    ct).ConfigureAwait(false);
                await _integrationRepository.AddAsync(integration, ct).ConfigureAwait(false);
                return integration;
            },
            async (committed, ct) => await _integrationRepository.GetByIdAsync(id, ct).ConfigureAwait(false)
                ?? throw new IntegrationNotFoundException(id),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 4). An early read and <c>integration.update</c> check answer a missing or
    /// forbidden request before a unit opens; they decide nothing. Inside the attempt, in this order:</para>
    /// <list type="number">
    /// <item>The ownership gate, when the request names the Active or Inactive state. A Retired integration becoming live is
    /// archive-blocking, and the current state is not known before the row is locked, so the decision comes from the request
    /// (gate first, then the row: the order every ownership writer uses).</item>
    /// <item>The integration row, locked to the end of the transaction, and <c>integration.update</c> authorized again against it
    /// as locked, so an ownership move that committed while this request waited is what the decision reads.</item>
    /// <item>The change is computed against the locked row: a request that changes nothing records nothing and returns the
    /// integration as it is; otherwise the reactivation guard runs, the one primary row is staged and the row is written.</item>
    /// </list>
    /// <para>The merge is made over the locked row, so two concurrent updates of different fields both survive. The primary row is
    /// named by the state transition when the state changed (<c>integration.activated</c>, <c>deactivated</c>, <c>retired</c>),
    /// otherwise <c>integration.updated</c>; either way the changed field names, never values, are recorded.</para>
    /// </remarks>
    public async ValueTask<DomainIntegration> UpdateIntegrationAsync(IntegrationId id, UpdateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var unit = _auditedUnitOfWork
            ?? throw new AuditInvariantException("Integration changes require the audited unit of work.");

        var existing = await _integrationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new IntegrationNotFoundException(id);
        await AuthorizeUpdateAsync(actor, existing, cancellationToken).ConfigureAwait(false);

        var needsGate = request.State is IntegrationState.Active or IntegrationState.Inactive;
        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationUpdated, AuditActor.CurrentPrincipal, AuditActionNames.IntegrationUpdateActions),
            async (transaction, ct) =>
            {
                if (needsGate)
                    await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                var fresh = await _integrationRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false)
                    ?? throw new IntegrationNotFoundException(id);
                await AuthorizeUpdateAsync(actor, fresh, ct).ConfigureAwait(false);

                var targetState = request.State ?? fresh.State;
                var stateChanges = targetState != fresh.State;
                var changed = new List<string>();
                if (request.DisplayName is not null && !string.Equals(request.DisplayName, fresh.DisplayName, StringComparison.Ordinal))
                    changed.Add("displayName");
                if (request.Settings is not null && !SettingsEqual(request.Settings, fresh.Settings))
                    changed.Add("settings");

                if (!stateChanges && changed.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return fresh;
                }

                // Reactivation guard (§6.5, D2): while an Application ResourceContext is archived, nothing it owns may become
                // live again. It fires on a change into Active and on Retired -> Inactive, and on no other update.
                if (stateChanges && targetState == IntegrationState.Active)
                {
                    await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                        fresh.ResourceContextId, _resourceContexts, "activating this integration", ct).ConfigureAwait(false);
                }
                else if (stateChanges && fresh.State == IntegrationState.Retired && targetState == IntegrationState.Inactive)
                {
                    await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                        fresh.ResourceContextId, _resourceContexts, "reactivating this integration", ct).ConfigureAwait(false);
                }

                var updated = fresh with
                {
                    DisplayName = request.DisplayName ?? fresh.DisplayName,
                    State = targetState,
                    Settings = request.Settings ?? fresh.Settings,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                var resourceContextId = fresh.ResourceContextId.Value;
                var auditEvent = stateChanges
                    ? AuditEvents.IntegrationStateChanged(
                        TransitionAction(targetState), id.Value, updated.DisplayName, updated.ModuleId, fresh.State, targetState, changed, resourceContextId)
                    : AuditEvents.IntegrationUpdated(id.Value, updated.DisplayName, updated.ModuleId, changed, resourceContextId);
                await transaction.StageAsync(auditEvent, ct).ConfigureAwait(false);
                await _integrationRepository.UpdateAsync(updated, ct).ConfigureAwait(false);
                return updated;
            },
            async (committed, ct) => await _integrationRepository.GetByIdAsync(id, ct).ConfigureAwait(false)
                ?? throw new IntegrationNotFoundException(id),
            cancellationToken).ConfigureAwait(false);
    }

    private static string TransitionAction(IntegrationState toState) => toState switch
    {
        IntegrationState.Active => AuditActionNames.IntegrationActivated,
        IntegrationState.Inactive => AuditActionNames.IntegrationDeactivated,
        _ => AuditActionNames.IntegrationRetired,
    };

    /// <summary>Two settings dictionaries are equal when they hold the same pairs; none and empty are the same (nothing is stored for either).</summary>
    private static bool SettingsEqual(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right)
    {
        var leftCount = left?.Count ?? 0;
        if (leftCount != (right?.Count ?? 0))
            return false;
        if (leftCount == 0)
            return true;
        foreach (var (key, value) in left!)
        {
            if (!right!.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private async ValueTask AuthorizeUpdateAsync(IActor actor, DomainIntegration integration, CancellationToken cancellationToken)
    {
        var identity = integration.GetIdentity();
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.IntegrationUpdate, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to update this integration.");
    }

    /// <inheritdoc />
    public ValueTask<DomainIntegration> ActivateIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
        UpdateIntegrationAsync(id, new UpdateIntegrationRequest(State: IntegrationState.Active), cancellationToken);

    /// <inheritdoc />
    public ValueTask<DomainIntegration> DeactivateIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
        UpdateIntegrationAsync(id, new UpdateIntegrationRequest(State: IntegrationState.Inactive), cancellationToken);

    /// <inheritdoc />
    public ValueTask<DomainIntegration> RetireIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
        UpdateIntegrationAsync(id, new UpdateIntegrationRequest(State: IntegrationState.Retired), cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 3, work package 4b; design gate §13.6). The unit owns the execution strategy
    /// and the transaction; its first statement is its own operation lock. Inside the attempt, in this order:</para>
    /// <list type="number">
    /// <item>The integration row lock (<c>UPDLOCK</c>, held to the commit or rollback): owner ruling D-4, the lock the WP3 writers,
    /// binding create, attached-schedule create and the ownership mover take. No ownership gate: the mover takes the gate and then
    /// this row, so a delete that took the gate after the row could deadlock with it, and the delete moves no ownership.</item>
    /// <item><c>integration.delete</c> authorized again against the row as locked, so an ownership move that committed while the
    /// delete waited is what the decision reads, and none can commit after it.</item>
    /// <item>The five blockers of the CTX-3B inventory. Every writer of a blocker row holds this row lock to its commit, so a count
    /// taken now sees every blocker that committed first and no blocker can commit before this transaction ends.</item>
    /// <item>The settings-secret disposition plan (owner ruling H1), refused as a whole before anything changes.</item>
    /// <item>The primary row, the removal of every persisted integration-scoped override by row id whatever its definition says,
    /// the retirement of the secrets the plan classified as safe (each with its Secrets-category row), then the integration row.</item>
    /// </list>
    /// <para>A verified commit after a lost acknowledgement is a success built from the committed primary row (matched by this
    /// operation's event id), never from the integration's absence. A missing integration is <see cref="IntegrationNotFoundException"/>;
    /// a blocker is <see cref="IntegrationDeleteBlockedException"/>; an undetermined credential disposition is
    /// <see cref="IntegrationSettingDispositionRefusedException"/>. Each of those changed nothing.</para>
    /// </remarks>
    public async ValueTask DeleteIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated actor.");

        var unit = _auditedUnitOfWork
            ?? throw new AuditInvariantException("Integration deletion requires the audited unit of work.");
        var classifier = _settingSecretClassifier
            ?? throw new AuditInvariantException("Integration deletion requires the integration setting secret classifier.");
        var retirement = _settingSecretRetirement
            ?? throw new AuditInvariantException("Integration deletion requires the integration setting secret retirement.");

        // An early answer for a missing integration or a caller with no delete permission on it, before a unit is opened. It
        // decides nothing: the unit reads the row again under its lock and authorizes again against that.
        var existing = await _integrationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new IntegrationNotFoundException(id);
        await AuthorizeDeleteAsync(actor, existing, cancellationToken).ConfigureAwait(false);

        await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.IntegrationDeleted, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // 1. The integration row, locked to the end of this transaction. Untracked, so a rerun reads it afresh.
                var integration = await _integrationRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false)
                    ?? throw new IntegrationNotFoundException(id);

                // 2. The decision is made on the integration as it is now: its owning context cannot move until the commit.
                await AuthorizeDeleteAsync(actor, integration, ct).ConfigureAwait(false);

                // 3. Every blocker, counted under the lock.
                await RequireNoBlockersAsync(id.Value, ct).ConfigureAwait(false);

                // 4. Which overrides to remove and what to do with the secrets they name, read in this transaction. An
                //    undetermined disposition refuses the whole delete here, before anything is staged or changed.
                // By the id the integration is stored under, not the spelling the caller used: that is what its overrides' scope and its
                // secrets' owner names carry (SEC-2.3).
                var plan = await classifier.ClassifyAsync(integration.Id.Value, ct).ConfigureAwait(false);
                plan.ThrowIfRefused();

                var resourceContextId = integration.ResourceContextId.Value;
                await transaction.StageAsync(
                    AuditEvents.IntegrationDeleted(
                        id.Value, integration.DisplayName, integration.ModuleId, plan.Overrides.Count, resourceContextId),
                    ct).ConfigureAwait(false);

                // 5. Every persisted override, by row id, then the secrets the plan allows, then the integration.
                await RemoveOverridesAsync(plan, ct).ConfigureAwait(false);
                await retirement.RetireAsync(transaction, plan, resourceContextId, ct).ConfigureAwait(false);

                var entity = await _db.Integrations.FirstOrDefaultAsync(e => e.Id == id.Value, ct).ConfigureAwait(false)
                    ?? throw new DbUpdateConcurrencyException("The integration was removed by another request while it was locked.");
                _db.Integrations.Remove(entity);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                return true;
            },
            // A verified commit is a successful delete, built from the committed primary row alone (the unit matched it by this
            // operation's event id). The integration is gone by design, so its absence is never read as a failed or missing delete.
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AuthorizeDeleteAsync(IActor actor, DomainIntegration integration, CancellationToken cancellationToken)
    {
        var identity = integration.GetIdentity();
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.IntegrationDelete, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to delete this integration.");
    }

    /// <summary>
    /// The five current-state blockers of the CTX-3B referential-lifecycle inventory, counted inside the delete's transaction while
    /// it holds the integration row lock. None has a foreign key; each is clearable by an operator.
    /// </summary>
    private async Task RequireNoBlockersAsync(string integrationId, CancellationToken ct)
    {
        var bindingCount = await _db.Bindings.CountAsync(e => e.IntegrationId == integrationId, ct).ConfigureAwait(false);
        if (bindingCount > 0)
            throw new IntegrationDeleteBlockedException(IntegrationDeleteBlocker.Bindings, bindingCount);

        var scheduleCount = await _db.ScheduleDefinitions.CountAsync(e => e.IntegrationId == integrationId, ct).ConfigureAwait(false);
        if (scheduleCount > 0)
            throw new IntegrationDeleteBlockedException(IntegrationDeleteBlocker.Schedules, scheduleCount);

        // Definitions via DELETE api/notification-definitions/{id}, subscriptions via the existing subscription removal, and
        // routing overrides via DELETE api/integrations/{id}/attention-routing are the operator's ways to clear these.
        var notificationDefinitionCount = await _db.NotificationDefinitions.CountAsync(e => e.IntegrationId == integrationId, ct).ConfigureAwait(false);
        if (notificationDefinitionCount > 0)
            throw new IntegrationDeleteBlockedException(IntegrationDeleteBlocker.NotificationDefinitions, notificationDefinitionCount);

        // Subscriptions carry their own nullable IntegrationId independently of NotificationDefinitionId, so an integration-scoped
        // subscription can exist under a platform- or module-scoped definition and is not covered by the blocker above. A
        // subscription with no integration id is not a blocker.
        var notificationSubscriptionCount = await _db.NotificationSubscriptions.CountAsync(e => e.IntegrationId == integrationId, ct).ConfigureAwait(false);
        if (notificationSubscriptionCount > 0)
            throw new IntegrationDeleteBlockedException(IntegrationDeleteBlocker.NotificationSubscriptions, notificationSubscriptionCount);

        var attentionRoutingCount = await _db.IntegrationAttentionRoutings.CountAsync(e => e.IntegrationId == integrationId, ct).ConfigureAwait(false);
        if (attentionRoutingCount > 0)
            throw new IntegrationDeleteBlockedException(IntegrationDeleteBlocker.AttentionRoutings, attentionRoutingCount);
    }

    /// <summary>
    /// Removes every override the plan lists, by row id, whatever its key's definition now says. Neither the per-key settings reset
    /// (which skips keys with no definition and retires a secret without looking for surviving references) nor a key-built row id
    /// is used: a historical row is removed as it was stored.
    /// </summary>
    /// <remarks>
    /// <para>No count of the integration's overrides is taken afterwards, and that is deliberate (AUD-1.4 slice 3, work package 5).
    /// <c>PlatformSettingValues</c> has no index that starts with the scope, so such a count reads the whole table, including the
    /// rows another integration's delete has just removed and still holds locked. Under locking READ COMMITTED two deletes of
    /// different integrations could then each wait on the other's rows and deadlock.</para>
    /// <para>The check added nothing the transaction does not already guarantee. The plan was read after the integration row lock
    /// was taken, with the predicate that selects an integration's overrides, and every writer of an integration-scoped override
    /// (the four settings changes) takes that row lock first, so none can commit a new override between the plan and the commit;
    /// the platform settings writer stores instance-scope rows only. Each row removed is deleted by its primary key and row version, so one
    /// that vanished or changed since the plan fails the save, and the row count is checked against the plan before anything is
    /// removed. A writer that bypassed the lock protocol is outside what this unit can coordinate and is not claimed to be caught.</para>
    /// </remarks>
    private async Task RemoveOverridesAsync(IntegrationSettingDeletionPlan plan, CancellationToken ct)
    {
        if (plan.Overrides.Count == 0)
            return;

        var rowIds = plan.Overrides.Select(o => o.RowId).ToList();
        var rows = await _db.PlatformSettingValues.Where(v => rowIds.Contains(v.Id)).ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count != rowIds.Count)
            throw new DbUpdateConcurrencyException("An integration setting was removed by another request during the delete.");

        _db.PlatformSettingValues.RemoveRange(rows);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
