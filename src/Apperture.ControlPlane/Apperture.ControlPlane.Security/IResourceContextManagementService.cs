using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorization- and audit-wrapped ResourceContext lifecycle operations (CTX-3A §9.1, §9.2).
/// </summary>
/// <remarks>
/// <para>
/// Every mutating method (<see cref="CreateAsync"/>, <see cref="UpdateAsync"/>,
/// <see cref="ArchiveAsync"/>, <see cref="UnarchiveAsync"/>) requires the caller to hold
/// <c>resource_context.manage</c> and appends an audit record on success. The underlying
/// <see cref="IResourceContextService"/> and <see cref="IResourceContextRepository"/> remain
/// authorization-free; this service is the only place that gate is enforced.
/// </para>
/// <para>
/// Per design gate §9.2, every read method (<see cref="ListAsync"/>, <see cref="GetAsync"/>,
/// <see cref="GetOwnershipSummaryAsync"/>, <see cref="GetOwnershipSummariesAsync"/>) performs
/// <b>no</b> authorization check of its own -- reading is gated only by <c>[Authorize]</c> at the
/// controller (any authenticated operator may read).
/// </para>
/// <para>
/// Mutating methods surface the same <see cref="InvalidOperationException"/>s (and, since CTX-3A
/// Task 5's relocation, <see cref="ResourceContextArchivedException"/>) that
/// <see cref="IResourceContextService"/> already throws -- this service does not swallow or
/// reclassify them, it only adds the authorization/audit wrapper around the delegate call.
/// </para>
/// </remarks>
public interface IResourceContextManagementService
{
    /// <summary>Lists ResourceContexts. No authorization check (§9.2).</summary>
    ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>Gets a ResourceContext by id, or null if it does not exist. No authorization check (§9.2).</summary>
    ValueTask<ResourceContext?> GetAsync(ResourceContextId id, CancellationToken cancellationToken = default);

    /// <summary>Creates a new Application ResourceContext. Requires <c>resource_context.manage</c>; audits on success.</summary>
    ValueTask<ResourceContext> CreateAsync(string name, string? slug, string? description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates name, slug and/or description of a ResourceContext. Requires <c>resource_context.manage</c>;
    /// audits on success, only when at least one field actually changed.
    /// </summary>
    ValueTask<ResourceContext> UpdateAsync(ResourceContextId id, string? name, string? slug, string? description, CancellationToken cancellationToken = default);

    /// <summary>Archives a ResourceContext. Requires <c>resource_context.manage</c>; audits on success.</summary>
    ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default);

    /// <summary>Returns an archived ResourceContext to Active. Requires <c>resource_context.manage</c>; audits on success.</summary>
    ValueTask<ResourceContext> UnarchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default);

    /// <summary>Gets the ownership summary for one ResourceContext. No authorization check (§9.2).</summary>
    ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default);

    /// <summary>Gets ownership summaries for every ResourceContext that owns at least one resource. No authorization check (§9.2).</summary>
    ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default);
}
