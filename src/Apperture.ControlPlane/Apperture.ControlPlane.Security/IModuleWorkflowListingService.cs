using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Lists modules and module workflows with permission enforcement.
/// GET /modules requires module.configure at instance; GET /modules/{id}/workflows requires workflow.execute at module:{id}.
/// </summary>
public interface IModuleWorkflowListingService
{
    /// <summary>
    /// Returns all known module manifests. Caller must have module.configure at instance scope.
    /// </summary>
    ValueTask<IReadOnlyList<ModuleCatalogEntry>> GetModulesAsync(CancellationToken cancellationToken = default);
    ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns workflow definitions for the given module. Caller must have workflow.execute at module:{moduleId} scope.
    /// </summary>
    ValueTask<IReadOnlyList<WorkflowDefinition>> GetWorkflowsForModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default);
    ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default);
}
