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
    /// Registers a permission with its product metadata. A definition without a display name or description is rejected, and
    /// registering the same identity again with different metadata is a conflict.
    /// </summary>
    /// <param name="definition">The permission definition.</param>
    void RegisterPermission(PermissionDefinition definition);

    /// <summary>
    /// Gets every registered permission definition, optionally filtered by family.
    /// </summary>
    IReadOnlyList<PermissionDefinition> GetPermissionDefinitions(ResourceFamily? family = null);

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

    /// <summary>
    /// Gets all registered resource descriptors with their family, optionally filtered by family.
    /// </summary>
    IReadOnlyList<(ResourceDescriptor Descriptor, ResourceFamily? Family)> GetResourceDescriptorsWithFamily(ResourceFamily? family = null);

    /// <summary>
    /// Gets all registered permission descriptors with their family, optionally filtered by family.
    /// </summary>
    IReadOnlyList<(PermissionDescriptor Descriptor, ResourceFamily? Family)> GetPermissionDescriptorsWithFamily(ResourceFamily? family = null);
}
