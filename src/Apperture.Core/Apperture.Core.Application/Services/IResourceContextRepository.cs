using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for ResourceContexts, the small ownership dimension table.
/// </summary>
/// <remarks>
/// There is deliberately <b>no delete operation</b>. ResourceContexts are archiveable, not
/// hard-deletable: historical provenance resolves a recorded ResourceContextId back to its row for
/// display long after the context stops owning anything, so a row must never disappear.
/// </remarks>
public interface IResourceContextRepository
{
    /// <summary>Gets a ResourceContext by id. Returns null when it does not exist.</summary>
    ValueTask<ResourceContext?> GetByIdAsync(ResourceContextId id, CancellationToken cancellationToken = default);

    /// <summary>Gets a ResourceContext by normalized slug. Returns null when it does not exist.</summary>
    ValueTask<ResourceContext?> GetBySlugAsync(string normalizedSlug, CancellationToken cancellationToken = default);

    /// <summary>Lists ResourceContexts, optionally excluding archived ones.</summary>
    ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived = true, CancellationToken cancellationToken = default);

    /// <summary>Adds a new ResourceContext.</summary>
    ValueTask AddAsync(ResourceContext resourceContext, CancellationToken cancellationToken = default);

    /// <summary>Updates name, slug, description or state of an existing ResourceContext.</summary>
    ValueTask UpdateAsync(ResourceContext resourceContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the Integrations, Connections and ScheduleDefinitions currently owned by this context.
    /// Used to keep archival conservative; it says nothing about historical ownership.
    /// </summary>
    ValueTask<int> CountOwnedResourcesAsync(ResourceContextId id, CancellationToken cancellationToken = default);
}
