namespace Apperture.Runtime.Abstractions;

public interface IWorkflowDefinitionRegistry
{
    IAsyncEnumerable<WorkflowDefinition> GetAllAsync(CancellationToken cancellationToken = default);
    ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default);
    IAsyncEnumerable<WorkflowDefinition> GetByModuleIdAsync(string moduleId, CancellationToken cancellationToken = default);
}
