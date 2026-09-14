using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Supported ResourceContext lifecycle operations.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no hard-delete operation. ResourceContexts are archiveable only; see
/// <see cref="ArchiveAsync"/>.
/// </para>
/// <para>
/// The reserved Shared context cannot be renamed, re-slugged or archived, and cannot be created a
/// second time. Those guards live here so every caller inherits them.
/// </para>
/// </remarks>
public interface IResourceContextService
{
    /// <summary>Gets a ResourceContext by id. Returns null when it does not exist.</summary>
    ValueTask<ResourceContext?> GetAsync(ResourceContextId id, CancellationToken cancellationToken = default);

    /// <summary>Gets a ResourceContext by slug. The value is normalized before lookup.</summary>
    ValueTask<ResourceContext?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Lists ResourceContexts, optionally excluding archived ones.</summary>
    ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an Application ResourceContext. The slug is normalized, must not be reserved, and must
    /// not already be taken. When no slug is supplied it is derived from the name.
    /// </summary>
    ValueTask<ResourceContext> CreateApplicationContextAsync(
        string name,
        string? slug = null,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a ResourceContext. Rejected for the reserved Shared context.</summary>
    ValueTask<ResourceContext> RenameAsync(ResourceContextId id, string name, CancellationToken cancellationToken = default);

    /// <summary>Changes the slug of a ResourceContext. Rejected for the reserved Shared context.</summary>
    ValueTask<ResourceContext> ChangeSlugAsync(ResourceContextId id, string slug, CancellationToken cancellationToken = default);

    /// <summary>Updates the optional description of a ResourceContext.</summary>
    ValueTask<ResourceContext> UpdateDescriptionAsync(ResourceContextId id, string? description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives a ResourceContext. Rejected for the reserved Shared context and for a context that
    /// currently owns any Integration, Connection or Schedule.
    /// </summary>
    ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default);

    /// <summary>Returns an archived ResourceContext to Active.</summary>
    ValueTask<ResourceContext> UnarchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the ResourceContext that will own a newly created resource.
    /// </summary>
    /// <param name="requestedResourceContextId">
    /// The context supplied by the caller, or null to default to the reserved Shared context.
    /// </param>
    /// <returns>The resolved, Active ResourceContext.</returns>
    /// <exception cref="InvalidOperationException">The context does not exist.</exception>
    /// <exception cref="ResourceContextArchivedException">The context exists but is archived.</exception>
    ValueTask<ResourceContext> ResolveForNewOwnershipAsync(
        ResourceContextId? requestedResourceContextId,
        CancellationToken cancellationToken = default);
}
