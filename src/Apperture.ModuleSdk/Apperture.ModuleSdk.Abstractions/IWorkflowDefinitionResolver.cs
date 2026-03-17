using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Resolves workflow definitions from the known modules set (DI-registered <see cref="IModule"/> instances).
/// Shared between control plane and instance plane to keep definition discovery consistent.
/// </summary>
public interface IWorkflowDefinitionResolver
{
    ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default);
}

