using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Lists modules and module workflows with authorization enforcement.
/// Uses module.configure for list modules and workflow.execute (module-declared) for list workflows per module.
/// Workflow catalog entries match host discovery; lifecycle activation still governs execution.
/// </summary>
public sealed class ModuleWorkflowListingService : IModuleWorkflowListingService
{
    private const string WorkflowExecuteAction = "workflow.execute";

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IEnumerable<IModule> _modules;
    private readonly IModuleRegistryService _moduleRegistryService;

    public ModuleWorkflowListingService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IEnumerable<IModule> modules,
        IModuleRegistryService moduleRegistryService)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _modules = modules ?? [];
        _moduleRegistryService = moduleRegistryService;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ModuleCatalogEntry>> GetModulesAsync(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ModuleConfigure, new ResourceDescriptor(ResourceTypeConstants.Module, ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to list modules.");

        await _moduleRegistryService.DiscoverModulesAsync(cancellationToken).ConfigureAwait(false);
        return await _moduleRegistryService.ListModulesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var modules = await GetModulesAsync(cancellationToken).ConfigureAwait(false);
        return modules.FirstOrDefault(x => string.Equals(x.Descriptor.ModuleId, moduleId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowDefinition>> GetWorkflowsForModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var moduleScopeKey = ScopeKeyConstants.Module(moduleId);
        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(WorkflowExecuteAction, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, moduleScopeKey)),
                null),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to list workflows for this module.");

        // Catalog: list definitions from any host-discovered module with this id. The module list API already
        // surfaces Discovered / InstalledDisabled entries; hiding workflows until EffectivelyActive made the
        // Workflow dropdown empty while HelloWorld was still selectable (operators enable modules on /modules).
        // Execution remains gated by IModuleActivationFilter + IWorkflowDefinitionResolver on start (Epic 27).
        var list = new List<WorkflowDefinition>();
        foreach (var module in _modules)
        {
            var manifest = module.GetManifest();
            if (!string.Equals(manifest.Id, moduleId, StringComparison.Ordinal))
                continue;
            await foreach (var definition in module.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false))
                list.Add(definition);
        }
        return list;
    }

    public async ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await EnsureConfigureAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        return await _moduleRegistryService.InstallModuleAsync(moduleId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await EnsureConfigureAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        return await _moduleRegistryService.EnableModuleAsync(moduleId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await EnsureConfigureAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        return await _moduleRegistryService.DisableModuleAsync(moduleId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await EnsureConfigureAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        return await _moduleRegistryService.UninstallModuleAsync(moduleId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsureConfigureAuthorizedAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ModuleConfigure, new ResourceDescriptor(ResourceTypeConstants.Module, ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to configure modules.");
    }
}
