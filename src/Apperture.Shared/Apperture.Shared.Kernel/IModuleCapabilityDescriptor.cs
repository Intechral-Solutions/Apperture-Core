namespace Apperture.Shared.Kernel;

/// <summary>
/// Minimal contract for something that declares module capabilities (resources, permissions). No platform lifecycle.
/// </summary>
public interface IModuleCapabilityDescriptor
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    IReadOnlyList<ResourceDescriptor> Resources { get; }
    IReadOnlyList<PermissionDescriptor> Permissions { get; }
}
