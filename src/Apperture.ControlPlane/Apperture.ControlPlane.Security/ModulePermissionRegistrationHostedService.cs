using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Runs once at startup to register module-declared permissions into the active IResourceDescriptorRegistry.
/// Depends on the registry (already seeded with platform permissions) and IModuleManifestProvider.
/// </summary>
public sealed class ModulePermissionRegistrationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ModulePermissionRegistrationHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IResourceDescriptorRegistry>();
        var manifestProvider = scope.ServiceProvider.GetRequiredService<IModuleManifestProvider>();
        var manifests = manifestProvider.GetManifests();
        ModulePermissionRegistrar.Register(registry, manifests);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
