using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Integration management workflow with authorization enforcement. Epic 49.
/// </summary>
public sealed class IntegrationManagementService : IIntegrationManagementService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IResourceContextService _resourceContexts;
    private readonly AppertureDbContext _db;
    private readonly IResourceOwnershipMutationGate _ownershipGate;
    private readonly IAuditService _audit;
    private readonly IIntegrationSettingsService _integrationSettings;

    public IntegrationManagementService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrationRepository,
        IResourceContextService resourceContexts,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate,
        IAuditService audit,
        IIntegrationSettingsService integrationSettings)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _integrationRepository = integrationRepository;
        _resourceContexts = resourceContexts;
        _db = db;
        _ownershipGate = ownershipGate;
        _audit = audit;
        // CTX-3B Plan 6a Task 5: no DI cycle -- IntegrationSettingsService depends on IIntegrationRepository,
        // not on IIntegrationManagementService.
        _integrationSettings = integrationSettings;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DomainIntegration>> ListIntegrationsAsync(CancellationToken cancellationToken = default) =>
        ListIntegrationsAsync(resourceContextId: null, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DomainIntegration>> ListIntegrationsAsync(
        ResourceContextId? resourceContextId,
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
        return await _integrationRepository.GetVisibleAsync(scopeKey, resourceContextId, cancellationToken);
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
    public async ValueTask DeleteIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default)
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
                new PermissionDescriptor(ActionConstants.IntegrationDelete, GovernedResourceDescriptor.FromIdentity(identity)),
                GovernedResourceDescriptor.FromIdentity(identity),
                identity),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to delete this integration.");

        var bindingCount = await _db.Bindings.CountAsync(e => e.IntegrationId == id.Value, cancellationToken);
        if (bindingCount > 0)
            throw new InvalidOperationException($"Cannot delete integration: {bindingCount} binding(s) reference it.");

        var scheduleCount = await _db.ScheduleDefinitions.CountAsync(e => e.IntegrationId == id.Value, cancellationToken);
        if (scheduleCount > 0)
            throw new InvalidOperationException($"Cannot delete integration: {scheduleCount} schedule(s) reference it.");

        // CTX-3B referential-lifecycle inventory (docs/.../ctx3b-integration-delete-referential-inventory.md):
        // none of these three has a foreign key, so nothing enforced them before. Block, matching the
        // existing Binding/Schedule precedent, rather than silently orphaning operator configuration.
        // Each blocker is clearable by an operator: definitions via DELETE api/notification-definitions/{id}
        // (Plan 6a Task 2), subscriptions via the existing subscription removal, and routing overrides via
        // DELETE api/integrations/{id}/attention-routing (Plan 6a Task 3).
        //
        // Every blocker runs -- and must pass -- before any cleanup or removal below: a refused delete
        // destroys nothing, least of all a setting value whose only pointer to an Active secret it holds.
        var notificationDefinitionCount = await _db.NotificationDefinitions.CountAsync(e => e.IntegrationId == id.Value, cancellationToken);
        if (notificationDefinitionCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete integration: {notificationDefinitionCount} notification definition(s) reference it. Delete them first.");

        // Subscriptions carry their own nullable IntegrationId independently of NotificationDefinitionId,
        // so an Integration-scoped subscription can exist under a Platform- or Module-scoped definition and
        // is NOT covered by the blocker above. See the inventory's Decisions section.
        var notificationSubscriptionCount = await _db.NotificationSubscriptions.CountAsync(e => e.IntegrationId == id.Value, cancellationToken);
        if (notificationSubscriptionCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete integration: {notificationSubscriptionCount} notification subscription(s) reference it. Remove them first.");

        var attentionRoutingCount = await _db.IntegrationAttentionRoutings.CountAsync(e => e.IntegrationId == id.Value, cancellationToken);
        if (attentionRoutingCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete integration: {attentionRoutingCount} attention routing override(s) reference it. Clear them first.");

        var entity = await _db.Integrations.FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken);
        if (entity is null)
            return;

        // Integration-scoped PlatformSettingValues have no independent meaning without the Integration
        // (unlike NotificationDefinitions/AttentionRoutings above, which are independently meaningful
        // operator configuration) -- clean them up rather than requiring separate manual deletion first.
        //
        // Delegated to IIntegrationSettingsService.ResetAsync rather than removing rows directly, because
        // a sensitive setting's row holds only an opaque ISecretStore reference: deleting the row alone
        // would orphan an Active secret permanently. ResetAsync is the established lifecycle -- delete the
        // scoped value, disable the owned secret via TryDisableOwnedSecretAsync, write the per-key audit --
        // and reusing it keeps secret disposal in the one place that owns it.
        //
        // Runs before the Integration row is removed, so ResetAsync's own audit can still resolve the
        // owning ResourceContext from the still-present Integration.
        //
        // Deliberately not atomic with the row delete below: the SQL store and ISecretStore are different
        // stores and do not share a transaction. This follows IntegrationSettingsService's existing reset
        // failure behavior rather than inventing new semantics; see the inventory doc.
        var scopedSettingKeys = await _db.PlatformSettingValues
            .Where(v => v.ScopeKind == PlatformSettingScopeConstants.IntegrationKind && v.ScopeId == id.Value)
            .Select(v => v.Key)
            .ToListAsync(cancellationToken);

        foreach (var key in scopedSettingKeys)
            await _integrationSettings.ResetAsync(id.Value, key, cancellationToken).ConfigureAwait(false);

        _db.Integrations.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        // Product decision P2: one audit record for the destructive operation itself. Records the owning
        // Context, the identity, and what was deliberately cleaned up alongside it. Appended after the
        // delete has committed and deliberately outside any transaction, matching this method's existing
        // shape: a lost audit row for a completed delete is strictly better than refusing the delete.
        await _audit.AppendAsync(new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actor.Id,
            ActionConstants.IntegrationDeleted,
            new ResourceDescriptor(ResourceTypeConstants.Integration, id.Value),
            Outcome: "",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            Details: $"Deleted integration '{existing.DisplayName}' ({id.Value}); cleaned up {scopedSettingKeys.Count} integration-scoped setting value(s).",
            ResourceContextId: existing.ResourceContextId.Value), cancellationToken).ConfigureAwait(false);
    }
}
