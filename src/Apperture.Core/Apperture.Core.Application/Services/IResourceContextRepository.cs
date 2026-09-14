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
    /// Gets the per-family live/retired-or-disabled ownership summary for this context: how many
    /// Integrations, Connections and ScheduleDefinitions it owns, split by whether each is currently
    /// live (Active integration/connection, enabled schedule) or not. Used by the "nothing live" archive
    /// rule (CTX-3A §6.5, decision D2), which blocks archive only on live ownership, not on retired
    /// Connections (never deletable) or disabled Schedules (never deletable). Says nothing about
    /// historical ownership.
    /// </summary>
    ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the ownership summary (see <see cref="GetOwnershipSummaryAsync"/>) for every ResourceContext
    /// that owns at least one resource, keyed by <see cref="ResourceContextId.Value"/>. Implementations
    /// must compute this with a constant number of queries (one GROUP BY per resource family), not one
    /// query per context, so callers such as an Applications list page can use it without an N+1 pattern.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default);
}
