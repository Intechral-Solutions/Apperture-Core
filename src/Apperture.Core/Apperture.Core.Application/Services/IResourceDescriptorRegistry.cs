using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Registers and resolves resource/permission descriptors (core + modules). Used by authorization and UI.
/// </summary>
public interface IResourceDescriptorRegistry
{
    /// <summary>
    /// Registers a resource descriptor.
    /// </summary>
    void RegisterResource(ResourceDescriptor descriptor);

    /// <summary>
    /// Registers a permission descriptor.
    /// </summary>
    void RegisterPermission(PermissionDescriptor descriptor);

    /// <summary>
    /// Gets all registered resource descriptors.
    /// </summary>
    IReadOnlyList<ResourceDescriptor> GetResourceDescriptors();

    /// <summary>
    /// Gets all registered permission descriptors.
    /// </summary>
    IReadOnlyList<PermissionDescriptor> GetPermissionDescriptors();
}
