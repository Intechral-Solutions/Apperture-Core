using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves workflow definition id to owning module id by enumerating known modules and their workflow definitions.
/// </summary>
public sealed class WorkflowModuleResolver : IWorkflowModuleResolver
{
    private readonly IEnumerable<IModule> _modules;

    public WorkflowModuleResolver(IEnumerable<IModule> modules)
    {
        _modules = modules ?? [];
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetModuleIdForWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        foreach (var module in _modules)
        {
            var manifest = module.GetManifest();
            await foreach (var definition in module.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(definition.Id.Value, workflowDefinitionId.Value, StringComparison.Ordinal))
                    return manifest.Id;
            }
        }
        return null;
    }
}
