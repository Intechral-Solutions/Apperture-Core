using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Application workflow for integration management. Epic 49.
/// </summary>
public interface IIntegrationManagementService
{
    /// <summary>
    /// Lists integrations visible to the current actor.
    /// </summary>
    ValueTask<IReadOnlyList<DomainIntegration>> ListIntegrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists integrations visible to the current actor, optionally restricted to one owning ResourceContext.
    /// </summary>
    /// <param name="resourceContextId">
    /// Owning ResourceContext to filter by, or null for the default global behavior (no context filter).
    /// </param>
    /// <remarks>
    /// The default implementation exists only so in-memory test doubles keep working; the production
    /// implementation pushes the filter down to the repository query.
    /// </remarks>
    async ValueTask<IReadOnlyList<DomainIntegration>> ListIntegrationsAsync(
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default)
    {
        var all = await ListIntegrationsAsync(cancellationToken).ConfigureAwait(false);
        if (resourceContextId is null)
            return all;
        return all.Where(i => string.Equals(i.ResourceContextId.Value, resourceContextId.Value, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Lists integrations visible to the current actor, optionally restricted to one owning ResourceContext
    /// and/or one Module. Both filters apply together (AND) when both are supplied.
    /// </summary>
    /// <param name="resourceContextId">
    /// Owning ResourceContext to filter by, or null for the default global behavior (no context filter).
    /// </param>
    /// <param name="moduleId">
    /// Module id to filter by, or null for no Module filter.
    /// </param>
    /// <remarks>
    /// The default implementation exists only so in-memory test doubles keep working; the production
    /// implementation pushes the filter down to the repository query.
    /// </remarks>
    async ValueTask<IReadOnlyList<DomainIntegration>> ListIntegrationsAsync(
        ResourceContextId? resourceContextId,
        string? moduleId,
        CancellationToken cancellationToken = default)
    {
        var all = await ListIntegrationsAsync(resourceContextId, cancellationToken).ConfigureAwait(false);
        if (moduleId is null)
            return all;
        return all.Where(i => string.Equals(i.ModuleId, moduleId, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Gets an integration by id. Returns null if not found or not visible.
    /// </summary>
    ValueTask<DomainIntegration?> GetIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new integration.
    /// </summary>
    ValueTask<DomainIntegration> CreateIntegrationAsync(CreateIntegrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates integration metadata.
    /// </summary>
    ValueTask<DomainIntegration> UpdateIntegrationAsync(IntegrationId id, UpdateIntegrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an integration to Active operational state.
    /// </summary>
    ValueTask<DomainIntegration> ActivateIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an integration to Inactive operational state while preserving configuration.
    /// </summary>
    ValueTask<DomainIntegration> DeactivateIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an integration to Retired operational state: deliberately removed from active service and
    /// retained for history, distinct from a temporary Inactive pause. Satisfies the Application archive
    /// rule (§6.5) once every owned Integration is Retired.
    /// </summary>
    ValueTask<DomainIntegration> RetireIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an integration. Fails if bindings or schedules reference it.
    /// </summary>
    ValueTask DeleteIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default);
}
