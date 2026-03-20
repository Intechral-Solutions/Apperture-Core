using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Implements IPlatformPermissionSeeder by delegating to PlatformPermissionSeeder.
/// </summary>
public sealed class PlatformPermissionSeederService : IPlatformPermissionSeeder
{
    private readonly IEnvironmentContext _environment;

    public PlatformPermissionSeederService(IEnvironmentContext environment)
    {
        _environment = environment;
    }

    /// <inheritdoc />
    public void Seed(IResourceDescriptorRegistry registry) =>
        PlatformPermissionSeeder.Seed(registry, _environment.EnvironmentId);
}
