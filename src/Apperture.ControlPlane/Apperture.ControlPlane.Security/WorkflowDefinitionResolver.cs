using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves workflow definitions from the known modules set (DI-registered IModule instances).
/// This keeps definition resolution aligned with module discovery without requiring a full registry implementation.
/// </summary>
public sealed class WorkflowDefinitionResolver : IWorkflowDefinitionResolver
{
    private readonly IEnumerable<IModule> _modules;

    public WorkflowDefinitionResolver(IEnumerable<IModule> modules)
    {
        _modules = modules ?? [];
    }

    public async ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default)
    {
        foreach (var module in _modules)
        {
            await foreach (var definition in module.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(definition.Id.Value, id.Value, StringComparison.Ordinal))
                    return definition;
            }
        }

        return null;
    }
}

