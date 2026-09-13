using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
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
    /// Gets integrations visible within the given scope, optionally restricted to one owning ResourceContext.
    /// </summary>
    /// <param name="resourceContextId">
    /// Owning ResourceContext to filter by, or null for the default global behavior (no context filter).
    /// </param>
    /// <remarks>
    /// A store-backed implementation must push the context filter into the query. The default
    /// implementation here exists only so in-memory test doubles keep working; it is not a query plan.
    /// </remarks>
    async ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(
        string scopeKey,
        ResourceContextId? resourceContextId,
        CancellationToken cancellationToken = default)
    {
        var all = await GetVisibleAsync(scopeKey, cancellationToken).ConfigureAwait(false);
        if (resourceContextId is null)
            return all;
        return all.Where(i => string.Equals(i.ResourceContextId.Value, resourceContextId.Value, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Adds a new integration.
    /// </summary>
    ValueTask AddAsync(DomainIntegration integration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing integration.
    /// </summary>
    ValueTask UpdateAsync(DomainIntegration integration, CancellationToken cancellationToken = default);
}
