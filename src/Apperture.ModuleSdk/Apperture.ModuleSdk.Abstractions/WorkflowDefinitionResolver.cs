using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Default implementation of <see cref="IWorkflowDefinitionResolver"/> that enumerates known modules.
/// Lifecycle-governed immediately via <see cref="IModuleActivationFilter"/> (Epic 27 — see docs/02-decisions/Epic27-Execution-Surface-Inventory.md).
/// </summary>
public sealed class WorkflowDefinitionResolver : IWorkflowDefinitionResolver
{
    private readonly IEnumerable<IModule> _modules;
    private readonly IModuleActivationFilter? _activationFilter;

    public WorkflowDefinitionResolver(IEnumerable<IModule> modules, IModuleActivationFilter? activationFilter = null)
    {
        _modules = modules ?? [];
        _activationFilter = activationFilter;
    }

    public async ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default)
    {
        foreach (var module in _modules)
        {
            var manifest = module.GetManifest();
            if (_activationFilter is not null && !await _activationFilter.IsEnabledAsync(manifest.Id, cancellationToken).ConfigureAwait(false))
                continue;
            await foreach (var definition in module.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(definition.Id.Value, id.Value, StringComparison.Ordinal))
                    return definition;
            }
        }

        return null;
    }
}

