using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// In-memory ResourceContext store for tests. Seeds the reserved Shared context exactly as the CTX-1
/// migration does, so tests exercise the same default-ownership path production takes.
/// </summary>
public sealed class InMemoryResourceContextRepository : IResourceContextRepository
{
    private readonly Dictionary<string, ResourceContext> _rows = new(StringComparer.Ordinal);

    /// <summary>Owned-resource counts by context id, so archive guards can be exercised.</summary>
    public Dictionary<string, int> OwnedResourceCounts { get; } = new(StringComparer.Ordinal);

    public InMemoryResourceContextRepository(bool seedShared = true)
    {
        if (seedShared)
            _rows[ResourceContextIds.SharedValue] = SharedContext();
    }

    /// <summary>The reserved Shared context exactly as the migration seeds it.</summary>
    public static ResourceContext SharedContext() => new(
        ResourceContextIds.Shared,
        "Shared",
        "shared",
        ResourceContextKinds.Shared,
        ResourceContextStates.Active,
        IsReserved: true,
        Description: null,
        CreatedAtUtc: DateTimeOffset.UnixEpoch,
        UpdatedAtUtc: DateTimeOffset.UnixEpoch);

    public ValueTask<ResourceContext?> GetByIdAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_rows.TryGetValue(id.Value, out var row) ? row : null);

    public ValueTask<ResourceContext?> GetBySlugAsync(string normalizedSlug, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_rows.Values.FirstOrDefault(r => string.Equals(r.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase)));

    public ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ResourceContext> rows = _rows.Values
            .Where(r => includeArchived || r.IsActive)
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        return ValueTask.FromResult(rows);
    }

    public ValueTask AddAsync(ResourceContext resourceContext, CancellationToken cancellationToken = default)
    {
        if (!_rows.TryAdd(resourceContext.Id.Value, resourceContext))
            throw new InvalidOperationException($"ResourceContext {resourceContext.Id.Value} already exists.");
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(ResourceContext resourceContext, CancellationToken cancellationToken = default)
    {
        if (!_rows.ContainsKey(resourceContext.Id.Value))
            throw new InvalidOperationException($"ResourceContext {resourceContext.Id.Value} not found.");
        _rows[resourceContext.Id.Value] = resourceContext;
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> CountOwnedResourcesAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(OwnedResourceCounts.TryGetValue(id.Value, out var count) ? count : 0);

    /// <summary>Inserts a context directly, bypassing the service guards (for arranging test state).</summary>
    public ResourceContext Seed(ResourceContext resourceContext)
    {
        _rows[resourceContext.Id.Value] = resourceContext;
        return resourceContext;
    }

    /// <summary>Arranges an Active Application context.</summary>
    public ResourceContext SeedApplication(string id, string name, string slug) => Seed(new ResourceContext(
        new ResourceContextId(id),
        name,
        slug,
        ResourceContextKinds.Application,
        ResourceContextStates.Active,
        IsReserved: false,
        Description: null,
        CreatedAtUtc: DateTimeOffset.UnixEpoch,
        UpdatedAtUtc: DateTimeOffset.UnixEpoch));
}

/// <summary>
/// Builds a real <see cref="ResourceContextService"/> over an in-memory store so tests exercise the
/// production guard logic rather than a mock of it.
/// </summary>
public static class ResourceContextTestFactory
{
    public static (ResourceContextService Service, InMemoryResourceContextRepository Repository) CreateWithShared()
    {
        var repository = new InMemoryResourceContextRepository();
        return (new ResourceContextService(repository, TimeProvider.System), repository);
    }

    /// <summary>Service with only the reserved Shared context present.</summary>
    public static IResourceContextService SharedOnly() => CreateWithShared().Service;
}
