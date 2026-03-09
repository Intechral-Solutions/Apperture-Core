namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Executes workflow steps.
/// Step parameter contracts should pass secret references and resolve them inside worker execution boundaries.
/// </summary>
public interface IWorkflowExecutor
{
    ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default);
}
