namespace Apperture.Core.Application.Execution;

/// <summary>
/// Contract for a workflow step handler. The executor invokes the handler with the step execution context and parameters.
/// The handler may call context.ResolveSlotAsync&lt;T&gt;(slotName) to obtain slot-bound dependencies.
/// </summary>
public interface IStepHandler
{
    /// <summary>
    /// Executes the step with the given context and parameters.
    /// </summary>
    /// <param name="context">Step execution context; use ResolveSlotAsync to obtain slot-bound dependencies.</param>
    /// <param name="parameters">Step parameters from the workflow invocation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Step result (or null). Must not return ResolvedSecret or secret material.
    /// Preferred explicit authoring:
    /// return <see cref="OperatorExecutionResult"/>.Success* for operator-visible success intent,
    /// and throw <see cref="OperatorExecutionResult"/>.Fail* (or <see cref="OperatorExecutionFailureException"/>) for declared failure intent.
    /// Backward-compatible behavior remains supported: plain successful returns and ordinary exceptions still work.
    /// <see cref="OperatorRunOutput"/> remains supported for success-path compatibility.
    /// </returns>
    ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}
