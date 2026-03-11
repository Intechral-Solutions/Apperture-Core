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
    /// <returns>Step result (or null). Must not return ResolvedSecret or secret material.</returns>
    ValueTask<object?> ExecuteAsync(
        IStepExecutionContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}
