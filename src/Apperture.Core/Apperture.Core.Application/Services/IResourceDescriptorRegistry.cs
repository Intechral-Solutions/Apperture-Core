using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Registers and resolves resource/permission descriptors (core + modules). Used by authorization and UI.
/// Supports optional ResourceFamily grouping for taxonomy.
/// </summary>
public interface IResourceDescriptorRegistry
{
    /// <summary>
    /// Registers a resource descriptor.
    /// </summary>
    /// <param name="descriptor">The resource descriptor.</param>
    /// <param name="family">Optional resource family for taxonomy grouping.</param>
    void RegisterResource(ResourceDescriptor descriptor, ResourceFamily? family = null);

    /// <summary>
    /// Registers a permission descriptor.
    /// </summary>
    /// <param name="descriptor">The permission descriptor.</param>
    /// <param name="family">Optional resource family for taxonomy grouping.</param>
    void RegisterPermission(PermissionDescriptor descriptor, ResourceFamily? family = null);

    /// <summary>
    /// Gets all registered resource descriptors, optionally filtered by family.
    /// </summary>
    IReadOnlyList<ResourceDescriptor> GetResourceDescriptors(ResourceFamily? family = null);

    /// <summary>
    /// Gets all registered permission descriptors, optionally filtered by family.
    /// </summary>
    IReadOnlyList<PermissionDescriptor> GetPermissionDescriptors(ResourceFamily? family = null);

    /// <summary>
    /// Returns true if the given permission descriptor is registered in the canonical catalog.
    /// Used to validate role-permission assignments before persistence.
    /// </summary>
    bool IsPermissionRegistered(PermissionDescriptor descriptor);
}
