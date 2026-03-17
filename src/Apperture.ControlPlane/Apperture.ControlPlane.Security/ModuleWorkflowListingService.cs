using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Lists modules and module workflows with authorization enforcement.
/// Uses module.configure for list modules and workflow.execute (module-declared) for list workflows per module.
/// </summary>
public sealed class ModuleWorkflowListingService : IModuleWorkflowListingService
{
    private const string WorkflowExecuteAction = "workflow.execute";

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IModuleManifestProvider _manifestProvider;
    private readonly IEnumerable<IModule> _modules;

    public ModuleWorkflowListingService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IModuleManifestProvider manifestProvider,
        IEnumerable<IModule> modules)
    {
        _principalContext = principalContext;
        _auth = auth;
        _manifestProvider = manifestProvider;
        _modules = modules ?? [];
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ModuleManifest>> GetModulesAsync(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.ModuleConfigure, new ResourceDescriptor(ResourceTypeConstants.Module, ScopeKeyConstants.Instance)),
            null,
            null,
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to list modules.");

        var manifests = _manifestProvider.GetManifests();
        return manifests.Select(m => m.Manifest).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowDefinition>> GetWorkflowsForModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var moduleScopeKey = ScopeKeyConstants.Module(moduleId);
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(WorkflowExecuteAction, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, moduleScopeKey)),
            null,
            null,
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to list workflows for this module.");

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
}
