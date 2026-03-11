using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Implements IPlatformPermissionSeeder by delegating to PlatformPermissionSeeder.
/// </summary>
public sealed class PlatformPermissionSeederService : IPlatformPermissionSeeder
{
    /// <inheritdoc />
    public void Seed(IResourceDescriptorRegistry registry) => PlatformPermissionSeeder.Seed(registry);
}
