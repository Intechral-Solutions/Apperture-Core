using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// Installs and enables Sample.HelloWorld, then registers module permissions.
/// Replaces <see cref="ModulePermissionRegistrationHostedService"/> in the governance host so the
/// permission catalog snapshot includes that module. The fixture's later row insert is too late:
/// <see cref="KnownModuleManifestProvider"/> only contributes modules that are already enabled.
/// </summary>
internal sealed class GovernanceHarnessModuleEnablementHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GovernanceHarnessModuleEnablementHostedService(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var moduleRegistry = scope.ServiceProvider.GetRequiredService<IModuleRegistryService>();
        const string moduleId = ManagementGovernanceTestConstants.HelloWorldModuleId;

        var install = await moduleRegistry.InstallModuleAsync(moduleId, cancellationToken).ConfigureAwait(false);
        if (!install.Succeeded)
            throw new InvalidOperationException(Describe("install", install));

        var enable = await moduleRegistry.EnableModuleAsync(moduleId, cancellationToken).ConfigureAwait(false);
        if (!enable.Succeeded)
            throw new InvalidOperationException(Describe("enable", enable));

        var registry = scope.ServiceProvider.GetRequiredService<IResourceDescriptorRegistry>();
        var manifestProvider = scope.ServiceProvider.GetRequiredService<IModuleManifestProvider>();
        await moduleRegistry.DiscoverModulesAsync(cancellationToken).ConfigureAwait(false);
        ModulePermissionRegistrar.Register(registry, manifestProvider.GetManifests());
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string Describe(string action, ModuleLifecycleTransitionResult result)
    {
        var blockers = result.Blockers.Count == 0
            ? "none"
            : string.Join("; ", result.Blockers.Select(b => $"{b.Code}: {b.Message}"));
        return $"Governance harness could not {action} {ManagementGovernanceTestConstants.HelloWorldModuleId}. Blockers: {blockers}";
    }
}
