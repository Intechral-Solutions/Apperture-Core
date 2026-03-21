using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves workflow definition id to owning module id by enumerating known modules and their workflow definitions.
/// Skips modules that are not effectively enabled for execution (see Epic27-Execution-Surface-Inventory.md).
/// </summary>
public sealed class WorkflowModuleResolver : IWorkflowModuleResolver
{
    private readonly IEnumerable<IModule> _modules;
    private readonly IModuleActivationFilter _activationFilter;

    public WorkflowModuleResolver(IEnumerable<IModule> modules, IModuleActivationFilter activationFilter)
    {
        _modules = modules ?? [];
        _activationFilter = activationFilter ?? throw new ArgumentNullException(nameof(activationFilter));
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetModuleIdForWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        foreach (var module in _modules)
        {
            var manifest = module.GetManifest();
            if (!await _activationFilter.IsEnabledAsync(manifest.Id, cancellationToken).ConfigureAwait(false))
                continue;

            await foreach (var definition in module.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(definition.Id.Value, workflowDefinitionId.Value, StringComparison.Ordinal))
                    return manifest.Id;
            }
        }

        return null;
    }
}
