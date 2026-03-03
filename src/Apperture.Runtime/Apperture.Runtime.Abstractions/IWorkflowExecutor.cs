namespace Apperture.Runtime.Abstractions;

public interface IWorkflowExecutor
{
    ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default);
}
