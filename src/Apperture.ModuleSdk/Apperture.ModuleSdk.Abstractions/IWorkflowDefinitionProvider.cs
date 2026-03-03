using Apperture.Runtime.Abstractions;

namespace Apperture.ModuleSdk.Abstractions;

public interface IWorkflowDefinitionProvider
{
    IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(CancellationToken cancellationToken = default);
}
