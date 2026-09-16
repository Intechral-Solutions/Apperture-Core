using Apperture.Core.Application.Integration;
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

    public IntegrationManagementService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrationRepository,
        IResourceContextService resourceContexts,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _integrationRepository = integrationRepository;
        _resourceContexts = resourceContexts;
        _db = db;
        _ownershipGate = ownershipGate;
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

        var entity = await _db.Integrations.FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken);
        if (entity is not null)
        {
            _db.Integrations.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
