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
    /// Gets integrations visible within the given scope, optionally restricted to one owning ResourceContext
    /// and/or one Module. Both filters apply together (AND) when both are supplied.
    /// </summary>
    /// <param name="resourceContextId">
    /// Owning ResourceContext to filter by, or null for the default global behavior (no context filter).
    /// </param>
    /// <param name="moduleId">
    /// Module id to filter by (matches <c>Integration.ModuleId</c>), or null for no Module filter.
    /// </param>
    /// <remarks>
    /// A store-backed implementation must push both filters into the query. The default implementation
    /// here exists only so in-memory test doubles keep working; it is not a query plan.
    /// </remarks>
    async ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(
        string scopeKey,
        ResourceContextId? resourceContextId,
        string? moduleId,
        CancellationToken cancellationToken = default)
    {
        var all = await GetVisibleAsync(scopeKey, resourceContextId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(moduleId))
            return all;
        return all.Where(i => string.Equals(i.ModuleId, moduleId, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Adds a new integration.
    /// </summary>
    ValueTask AddAsync(DomainIntegration integration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing integration.
    /// </summary>
    ValueTask UpdateAsync(DomainIntegration integration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an Integration for an ownership-sensitive write, taking a row-level update lock held for the
    /// lifetime of the caller's ambient transaction on this same <c>AppertureDbContext</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Must be called inside an already-open transaction, <b>after</b>
    /// <c>IResourceOwnershipMutationGate.AcquireAsync</c>. The gate is what makes the operation correct;
    /// this row lock is defense in depth against a future path that skips the gate.
    /// </para>
    /// <para>
    /// The result is <b>not tracked</b>. `EnableRetryOnFailure` is active in production, so a rolled-back
    /// attempt must leave nothing in the change tracker for the retried attempt to read back as if it were
    /// current state.
    /// </para>
    /// <para>
    /// On the EF Core InMemory provider (unit tests) this degrades to a plain untracked read with no real
    /// lock — serialization itself can only be proven against real SQL Server.
    /// </para>
    /// </remarks>
    ValueTask<DomainIntegration?> GetByIdForUpdateAsync(IntegrationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes only the owning <c>ResourceContextId</c> column. The only supported way to change an
    /// Integration's ownership (CTX-3B); <see cref="UpdateAsync"/> deliberately never touches this column.
    /// Must be called inside the same gated transaction as the preceding
    /// <see cref="GetByIdForUpdateAsync"/>.
    /// </summary>
    /// <remarks>
    /// Implemented with <c>ExecuteUpdateAsync</c> on relational providers, so it bypasses the change
    /// tracker entirely: it writes exactly one column, never flushes unrelated pending changes on the
    /// shared request-scoped context, and leaves no tracked state behind for an execution-strategy retry.
    /// </remarks>
    ValueTask SetResourceContextIdAsync(IntegrationId id, ResourceContextId resourceContextId, CancellationToken cancellationToken = default);
}
