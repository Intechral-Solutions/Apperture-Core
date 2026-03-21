using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Runs once at startup to register module-declared permissions into the active IResourceDescriptorRegistry.
/// Depends on the registry (already seeded with platform permissions) and IModuleManifestProvider.
/// Restart-bound for permission catalog updates: re-run requires process restart (Epic 27 — Epic27-Execution-Surface-Inventory.md).
/// </summary>
public sealed class ModulePermissionRegistrationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModulePermissionRegistrationHostedService> _logger;

    public ModulePermissionRegistrationHostedService(IServiceScopeFactory scopeFactory, ILogger<ModulePermissionRegistrationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IResourceDescriptorRegistry>();
        var manifestProvider = scope.ServiceProvider.GetRequiredService<IModuleManifestProvider>();
        var moduleRegistry = scope.ServiceProvider.GetRequiredService<IModuleRegistryService>();
        moduleRegistry.DiscoverModulesAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
        var manifests = manifestProvider.GetManifests();
        try
        {
            ModulePermissionRegistrar.Register(registry, manifests);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module governance contribution registration failed at startup.");
            throw;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
