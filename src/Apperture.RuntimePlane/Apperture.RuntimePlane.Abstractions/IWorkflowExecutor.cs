namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Executes workflow steps by creating an IStepExecutionContext (with ModuleId from the definition), looking up the step handler, and invoking it with that context.
/// Steps obtain slot-bound dependencies only via context.ResolveSlotAsync(slotName).
/// </summary>
/// <remarks>
/// Steps do not receive ISecretResolver; connection secrets are resolved inside the resolution pipeline, not in step code.
/// Steps receive only IStepExecutionContext. Step results must not return ResolvedSecret or secret material.
/// Preferred explicit authoring contract is Apperture.Core.Application.Execution.OperatorExecutionResult:
/// handlers return Success* for operator-visible success intent and throw Fail* (or OperatorExecutionFailureException) for declared failure intent.
/// Backward-compatible behavior remains supported: plain successful returns and ordinary exceptions still work.
/// OperatorRunOutput also remains supported for success-path compatibility.
/// </remarks>
public interface IWorkflowExecutor
{
    ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default) =>
        ExecuteStepAsync(
            definition,
            stepName,
            instance,
            parameters,
            integrationId: null,
            executionScope: null,
            cancellationToken);

    /// <summary>
    /// Executes a step with optional integration scope for slot resolution (Epic 54).
    /// </summary>
    ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        string? integrationId,
        CancellationToken cancellationToken) =>
        ExecuteStepAsync(
            definition,
            stepName,
            instance,
            parameters,
            integrationId,
            executionScope: null,
            cancellationToken);

    /// <summary>
    /// Executes a step with optional integration scope (Epic 54) and correlation scope for platform signals (Epic 77 Phase 2).
    /// </summary>
    ValueTask<object?> ExecuteStepAsync(
        WorkflowDefinition definition,
        string stepName,
        WorkflowInstance instance,
        IReadOnlyDictionary<string, object?>? parameters,
        string? integrationId,
        WorkflowStepExecutionScope? executionScope,
        CancellationToken cancellationToken);
}
