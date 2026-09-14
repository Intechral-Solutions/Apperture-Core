using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.ResourceContexts;

/// <summary>
/// Supported ResourceContext lifecycle, with the reserved-context and slug guards applied in one place.
/// </summary>
/// <remarks>
/// <para>
/// No hard delete exists here by design. A foreign key protects ownership integrity structurally, but
/// it only proves that a context owns nothing <i>right now</i>; it cannot prove the context never owned
/// anything. Historical provenance must still be able to resolve a recorded id to a row, so retirement
/// is <see cref="ResourceContextStates.Archived"/>.
/// </para>
/// <para>
/// This service performs no authorization. ResourceContext ownership and authorization ScopeKeys are
/// separate concepts and CTX-1 changes no authorization semantics.
/// </para>
/// </remarks>
public sealed class ResourceContextService : IResourceContextService
{
    private readonly IResourceContextRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ResourceContextService(IResourceContextRepository repository, TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public ValueTask<ResourceContext?> GetAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
        _repository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public ValueTask<ResourceContext?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!ResourceContextSlug.TryNormalize(slug, out var normalized))
            return ValueTask.FromResult<ResourceContext?>(null);
        return _repository.GetBySlugAsync(normalized, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived = true, CancellationToken cancellationToken = default) =>
        _repository.ListAsync(includeArchived, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ResourceContext> CreateApplicationContextAsync(
        string name,
        string? slug = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("ResourceContext name is required.");

        var normalizedSlug = NormalizeOrThrow(slug ?? name);
        await EnsureSlugAvailableAsync(normalizedSlug, currentId: null, cancellationToken).ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();
        var created = new ResourceContext(
            new ResourceContextId(Guid.NewGuid().ToString("N")[..24]),
            name.Trim(),
            normalizedSlug,
            ResourceContextKinds.Application,
            ResourceContextStates.Active,
            IsReserved: false,
            Description: string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        await _repository.AddAsync(created, cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext> RenameAsync(ResourceContextId id, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("ResourceContext name is required.");

        var existing = await GetRequiredAsync(id, cancellationToken).ConfigureAwait(false);
        EnsureNotReserved(existing, "renamed");

        return await SaveAsync(existing with { Name = name.Trim() }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext> ChangeSlugAsync(ResourceContextId id, string slug, CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredAsync(id, cancellationToken).ConfigureAwait(false);
        EnsureNotReserved(existing, "re-slugged");

        var normalizedSlug = NormalizeOrThrow(slug);
        if (string.Equals(normalizedSlug, existing.Slug, StringComparison.Ordinal))
            return existing;

        await EnsureSlugAvailableAsync(normalizedSlug, existing.Id, cancellationToken).ConfigureAwait(false);
        return await SaveAsync(existing with { Slug = normalizedSlug }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext> UpdateDescriptionAsync(ResourceContextId id, string? description, CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredAsync(id, cancellationToken).ConfigureAwait(false);
        var next = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return await SaveAsync(existing with { Description = next }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredAsync(id, cancellationToken).ConfigureAwait(false);
        EnsureNotReserved(existing, "archived");

        if (!existing.IsActive)
            return existing;

        var summary = await _repository.GetOwnershipSummaryAsync(existing.Id, cancellationToken).ConfigureAwait(false);
        if (summary.BlocksArchive)
        {
            throw new InvalidOperationException(
                $"Cannot archive ResourceContext '{existing.Name}' ({existing.Id.Value}): it still has " +
                $"{summary.LiveIntegrations} active integration(s), {summary.ActiveConnections} active connection(s) " +
                $"and {summary.EnabledSchedules} enabled schedule(s). Retire or disable them first.");
        }

        return await SaveAsync(existing with { State = ResourceContextStates.Archived }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext> UnarchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing.IsActive)
            return existing;

        return await SaveAsync(existing with { State = ResourceContextStates.Active }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceContext> ResolveForNewOwnershipAsync(
        ResourceContextId? requestedResourceContextId,
        CancellationToken cancellationToken = default)
    {
        var id = requestedResourceContextId ?? ResourceContextIds.Shared;
        var resolved = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ResourceContext '{id.Value}' not found.");

        if (!resolved.IsActive)
        {
            throw new ResourceContextArchivedException(
                resolved.Id,
                resolved.Name,
                $"ResourceContext '{resolved.Name}' ({resolved.Id.Value}) is archived and cannot own new resources.");
        }

        return resolved;
    }

    private async ValueTask<ResourceContext> GetRequiredAsync(ResourceContextId id, CancellationToken cancellationToken) =>
        await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ResourceContext '{id.Value}' not found.");

    private async ValueTask<ResourceContext> SaveAsync(ResourceContext next, CancellationToken cancellationToken)
    {
        var stamped = next with { UpdatedAtUtc = _timeProvider.GetUtcNow() };
        await _repository.UpdateAsync(stamped, cancellationToken).ConfigureAwait(false);
        return stamped;
    }

    private static string NormalizeOrThrow(string? candidate)
    {
        if (!ResourceContextSlug.TryNormalize(candidate, out var normalized))
            throw new InvalidOperationException($"'{candidate}' does not produce a usable ResourceContext slug.");
        return normalized;
    }

    private static void EnsureNotReserved(ResourceContext context, string operation)
    {
        if (context.IsReserved)
            throw new InvalidOperationException($"The reserved ResourceContext '{context.Name}' cannot be {operation}.");
    }

    private async ValueTask EnsureSlugAvailableAsync(string normalizedSlug, ResourceContextId? currentId, CancellationToken cancellationToken)
    {
        if (ResourceContextSlug.IsReserved(normalizedSlug))
            throw new InvalidOperationException($"ResourceContext slug '{normalizedSlug}' is reserved.");

        var existing = await _repository.GetBySlugAsync(normalizedSlug, cancellationToken).ConfigureAwait(false);
        if (existing is not null && (currentId is null || !string.Equals(existing.Id.Value, currentId.Value, StringComparison.Ordinal)))
            throw new InvalidOperationException($"ResourceContext slug '{normalizedSlug}' is already in use.");
    }
}
