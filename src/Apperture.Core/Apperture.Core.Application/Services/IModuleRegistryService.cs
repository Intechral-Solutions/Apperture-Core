using Apperture.Core.Domain.Environment;
using Apperture.ModuleSdk.Abstractions;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Resolves installed modules and instances for an Environment. Integrates with ModuleSdk discovery.
/// </summary>
public interface IModuleRegistryService
{
    ValueTask<IReadOnlyList<ModuleCatalogEntry>> ListModulesAsync(CancellationToken cancellationToken = default);
    ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ModuleDescriptor>> GetEnabledDescriptorsAsync(CancellationToken cancellationToken = default);
    ValueTask<ModuleLifecycleTransitionResult> DiscoverModulesAsync(CancellationToken cancellationToken = default);
    ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default);
}
