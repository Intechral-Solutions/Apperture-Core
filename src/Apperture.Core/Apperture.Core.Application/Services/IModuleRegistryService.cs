using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Resolves installed modules and instances for an Environment. Integrates with ModuleSdk discovery.
/// </summary>
public interface IModuleRegistryService
{
    /// <summary>
    /// Gets all module installations for an environment.
    /// </summary>
    ValueTask<IReadOnlyList<ModuleInstallation>> GetInstallationsAsync(EnvironmentId environmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a module instance by id.
    /// </summary>
    ValueTask<ModuleInstance?> GetInstanceAsync(ModuleInstanceId moduleInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a module in the catalog.
    /// </summary>
    ValueTask RegisterModuleAsync(Module module, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a module installation in an environment.
    /// </summary>
    ValueTask EnableModuleAsync(EnvironmentId environmentId, ModuleId moduleId, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a module installation.
    /// </summary>
    ValueTask DisableModuleAsync(ModuleInstallationId moduleInstallationId, CancellationToken cancellationToken = default);
}
