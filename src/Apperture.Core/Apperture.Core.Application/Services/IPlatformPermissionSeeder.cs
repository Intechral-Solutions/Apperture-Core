namespace Apperture.Core.Application.Services;

/// <summary>
/// Seeds the platform permission and resource catalog into IResourceDescriptorRegistry.
/// Uses ActionConstants and ResourceTypeConstants. Per ADR-0013.
/// </summary>
public interface IPlatformPermissionSeeder
{
    /// <summary>
    /// Registers initial platform permission and resource descriptors with the registry.
    /// Idempotent; safe to call multiple times.
    /// </summary>
    void Seed(IResourceDescriptorRegistry registry);
}
