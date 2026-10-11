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
/// Integration management workflow with authorization enforcement. Epic 49. The hard-delete is one audited unit (AUD-1.4 slice 3,
/// work package 4b): the integration, its setting overrides, the secrets they owned and the audit rows commit together or not at all.
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
        // The delete's collaborators. Optional so the services that only create, read and update integrations need none of
        // them; the delete refuses to run without all three rather than delete unaudited.
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
    public async ValueTask<DomainIntegration> CreateIntegrationAsync(CreateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var scopeKey = request.ScopeKey ?? ScopeKeyConstants.Instance;
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.IntegrationCreate, new ResourceDescriptor(ResourceTypeConstants.Integration, scopeKey)),
                null),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to create integrations.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Gate first: a new Active Integration is an archive-blocking resource entering a context, so
            // this must not interleave with a concurrent Archive evaluating that same context's ownership
            // summary (CTX-3B follow-on: closes the create-vs-archive race Plan 2's own final review flagged).
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            // Ownership defaults to the reserved Shared ResourceContext when the caller supplies none, so
            // existing create clients stay compatible. A supplied context must exist and be Active.
            var owningContext = await _resourceContexts.ResolveForNewOwnershipAsync(request.ResourceContextId, cancellationToken).ConfigureAwait(false);

            var id = new IntegrationId(Guid.NewGuid().ToString("N")[..24]);
            var now = DateTimeOffset.UtcNow;
            var integration = new DomainIntegration(id, request.DisplayName, request.ModuleId, request.ScopeKey, IntegrationState.Active, request.Settings, now, now)
            {
                ResourceContextId = owningContext.Id
            };

            await _integrationRepository.AddAsync(integration, cancellationToken).ConfigureAwait(false);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return integration;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<DomainIntegration> UpdateIntegrationAsync(IntegrationId id, UpdateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var existing = await _integrationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Integration {id.Value} not found.");

        var identity = existing.GetIdentity();
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.IntegrationUpdate, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to update this integration.");

        // Retired -> {Active, Inactive} both flip this Integration from non-blocking to archive-blocking
        // (ResourceContextOwnershipSummary.BlocksArchive counts anything that is not Retired), so both
        // transitions must run under the ownership gate to avoid racing a concurrent Archive that reads an
        // empty ownership summary (CTX-3B follow-on). This is wider than the pre-existing CTX-3A
        // reactivation guard below, which only ever fired on the "can dispatch" definition of live
        // (any state -> Active).
        var becomesArchiveBlocking = request.State is not null
            && request.State != IntegrationState.Retired
            && existing.State == IntegrationState.Retired;

        if (!becomesArchiveBlocking)
        {
            // No archive-blocking transition: behave exactly as before the CTX-3B follow-on -- no gate,
            // no transaction, so ordinary field-only updates carry no added overhead.

            // Reactivation guard (§6.5, D2): while an Application ResourceContext is archived, nothing
            // it owns may become live again. Only fires on the transition INTO Active; other field
            // updates on an already-non-Active integration under an archived context remain allowed.
            if (request.State == IntegrationState.Active && existing.State != IntegrationState.Active)
            {
                await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                    existing.ResourceContextId, _resourceContexts, "activating this integration", cancellationToken).ConfigureAwait(false);
            }

            var updated = MergeUpdate(existing, request);
            await _integrationRepository.UpdateAsync(updated, cancellationToken);
            return updated;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // Gate first: Retired -> {Active, Inactive} makes this Integration archive-blocking, so this
            // must not interleave with a concurrent Archive evaluating the owning context's ownership
            // summary (CTX-3B follow-on).
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            // Re-read fresh: `existing` was captured before the gate and may be stale.
            var fresh = await _integrationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Integration {id.Value} not found.");

            var actionPhrase = request.State == IntegrationState.Active
                ? "activating this integration"
                : "reactivating this integration";
            await ResourceContextReactivationGuard.EnsureOwningContextIsActiveAsync(
                fresh.ResourceContextId, _resourceContexts, actionPhrase, cancellationToken).ConfigureAwait(false);

            var updated = MergeUpdate(fresh, request);
            await _integrationRepository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return updated;
        }).ConfigureAwait(false);
    }

    private static DomainIntegration MergeUpdate(DomainIntegration current, UpdateIntegrationRequest request) =>
        current with
        {
            DisplayName = request.DisplayName ?? current.DisplayName,
            State = request.State ?? current.State,
            Settings = request.Settings ?? current.Settings,
            UpdatedAt = DateTimeOffset.UtcNow
        };

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
                var plan = await classifier.ClassifyAsync(id.Value, ct).ConfigureAwait(false);
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
