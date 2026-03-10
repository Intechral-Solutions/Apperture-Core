using System.Collections.Concurrent;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Thread-safe in-memory implementation of IResourceDescriptorRegistry.
/// Used for Phase 1 scaffolding; seeded with platform permission catalog at startup.
/// </summary>
public sealed class InMemoryResourceDescriptorRegistry : IResourceDescriptorRegistry
{
    private readonly ConcurrentBag<(ResourceDescriptor Descriptor, ResourceFamily? Family)> _resources = [];
    private readonly ConcurrentBag<(PermissionDescriptor Descriptor, ResourceFamily? Family)> _permissions = [];

    /// <inheritdoc />
    public void RegisterResource(ResourceDescriptor descriptor, ResourceFamily? family = null) =>
        _resources.Add((descriptor, family));

    /// <inheritdoc />
    public void RegisterPermission(PermissionDescriptor descriptor, ResourceFamily? family = null) =>
        _permissions.Add((descriptor, family));

    /// <inheritdoc />
    public IReadOnlyList<ResourceDescriptor> GetResourceDescriptors(ResourceFamily? family = null) =>
        family is null
            ? _resources.Select(r => r.Descriptor).ToList()
            : _resources.Where(r => r.Family == family).Select(r => r.Descriptor).ToList();

    /// <inheritdoc />
    public IReadOnlyList<PermissionDescriptor> GetPermissionDescriptors(ResourceFamily? family = null) =>
        family is null
            ? _permissions.Select(p => p.Descriptor).ToList()
            : _permissions.Where(p => p.Family == family).Select(p => p.Descriptor).ToList();
}
