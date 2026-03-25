using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
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
    private readonly AppertureDbContext _db;

    public IntegrationManagementService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IIntegrationRepository integrationRepository,
        AppertureDbContext db)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _integrationRepository = integrationRepository;
        _db = db;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DomainIntegration>> ListIntegrationsAsync(CancellationToken cancellationToken = default)
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
        return await _integrationRepository.GetVisibleAsync(scopeKey, cancellationToken);
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

        var id = new IntegrationId(Guid.NewGuid().ToString("N")[..24]);
        var now = DateTimeOffset.UtcNow;
        var integration = new DomainIntegration(
            id,
            request.DisplayName,
            request.ModuleId,
            request.ScopeKey,
            IntegrationState.Active,
            request.Settings,
            now,
            now);

        await _integrationRepository.AddAsync(integration, cancellationToken);
        return integration;
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

        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            DisplayName = request.DisplayName ?? existing.DisplayName,
            State = request.State ?? existing.State,
            Settings = request.Settings ?? existing.Settings,
            UpdatedAt = now
        };

        await _integrationRepository.UpdateAsync(updated, cancellationToken);
        return updated;
    }

    /// <inheritdoc />
    public ValueTask<DomainIntegration> ActivateIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
        UpdateIntegrationAsync(id, new UpdateIntegrationRequest(State: IntegrationState.Active), cancellationToken);

    /// <inheritdoc />
    public ValueTask<DomainIntegration> DeactivateIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
        UpdateIntegrationAsync(id, new UpdateIntegrationRequest(State: IntegrationState.Inactive), cancellationToken);

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
