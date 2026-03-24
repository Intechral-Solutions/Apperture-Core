using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
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
    /// Deletes an integration. Fails if bindings or schedules reference it.
    /// </summary>
    ValueTask DeleteIntegrationAsync(IntegrationId id, CancellationToken cancellationToken = default);
}
