using Apperture.Core.Domain.Integration;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for Integration resources. Epic 49.
/// </summary>
public interface IIntegrationRepository
{
    /// <summary>
    /// Gets an integration by id.
    /// </summary>
    ValueTask<DomainIntegration?> GetByIdAsync(IntegrationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets integrations visible within the given scope (instance or contained scopes).
    /// </summary>
    ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new integration.
    /// </summary>
    ValueTask AddAsync(DomainIntegration integration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing integration.
    /// </summary>
    ValueTask UpdateAsync(DomainIntegration integration, CancellationToken cancellationToken = default);
}
