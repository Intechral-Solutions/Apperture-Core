using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorize-then-delegate wrapper around <see cref="IResourceContextService"/> (CTX-3A §9.1),
/// following the same pattern as <see cref="IntegrationManagementService.CreateIntegrationAsync"/>
/// (authorize via <see cref="IAuthorizationEvaluationService"/>, then delegate) combined with
/// <see cref="PlatformSettingsService"/>'s audit-append shape.
/// </summary>
public sealed class ResourceContextManagementService : IResourceContextManagementService
{
    private const string AuditOutcome = "resource-context-management";

    private readonly IResourceContextService _resourceContexts;
    private readonly IResourceContextRepository _resourceContextRepository;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IAuditService _audit;

    public ResourceContextManagementService(
        IResourceContextService resourceContexts,
        IResourceContextRepository resourceContextRepository,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IAuditService audit)
    {
        _resourceContexts = resourceContexts;
        _resourceContextRepository = resourceContextRepository;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _audit = audit;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        _resourceContexts.ListAsync(includeArchived, cancellationToken);

    /// <inheritdoc />
    public ValueTask<ResourceContext?> GetAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
        _resourceContexts.GetAsync(id, cancellationToken);

    /// <inheritdoc />
    public ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
        _resourceContextRepository.GetOwnershipSummaryAsync(id, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default) =>
        _resourceContextRepository.GetOwnershipSummariesAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ResourceContext> CreateAsync(string name, string? slug, string? description, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var created = await _resourceContexts.CreateApplicationContextAsync(name, slug, description, cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync("resource_context.created", actor.Id, created.Id, $"Created Application '{created.Name}' ({created.Slug}).", cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext> UpdateAsync(ResourceContextId id, string? name, string? slug, string? description, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);

        var current = await _resourceContexts.GetAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ResourceContext '{id.Value}' not found.");

        var changedFields = new List<string>();

        if (name is not null && !string.Equals(name, current.Name, StringComparison.Ordinal))
        {
            current = await _resourceContexts.RenameAsync(id, name, cancellationToken).ConfigureAwait(false);
            changedFields.Add("name");
        }

        if (slug is not null && !string.Equals(slug, current.Slug, StringComparison.Ordinal))
        {
            current = await _resourceContexts.ChangeSlugAsync(id, slug, cancellationToken).ConfigureAwait(false);
            changedFields.Add("slug");
        }

        if (description is not null && !string.Equals(description, current.Description, StringComparison.Ordinal))
        {
            current = await _resourceContexts.UpdateDescriptionAsync(id, description, cancellationToken).ConfigureAwait(false);
            changedFields.Add("description");
        }

        if (changedFields.Count > 0)
        {
            await AppendAuditAsync("resource_context.updated", actor.Id, id, $"Changed fields: {string.Join(", ", changedFields)}.", cancellationToken).ConfigureAwait(false);
        }

        return current;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var archived = await _resourceContexts.ArchiveAsync(id, cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync("resource_context.archived", actor.Id, id, $"Archived '{archived.Name}'.", cancellationToken).ConfigureAwait(false);
        return archived;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext> UnarchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var unarchived = await _resourceContexts.UnarchiveAsync(id, cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync("resource_context.unarchived", actor.Id, id, $"Unarchived '{unarchived.Name}'.", cancellationToken).ConfigureAwait(false);
        return unarchived;
    }

    private async ValueTask<IActor> RequireManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ResourceContextManage, new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage applications.");

        return actor;
    }

    private async ValueTask AppendAuditAsync(string action, ActorId actorId, ResourceContextId resourceContextId, string details, CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            action,
            new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance),
            AuditOutcome,
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details,
            ResourceContextId: resourceContextId.Value);
        await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
    }
}
