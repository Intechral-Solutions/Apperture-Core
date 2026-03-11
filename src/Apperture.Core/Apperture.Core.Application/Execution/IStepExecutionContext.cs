namespace Apperture.Core.Application.Execution;

/// <summary>
/// Execution context for a workflow step. Supplies the module id and slot-bound dependency resolution.
/// Step code receives this from the executor; it does not receive connection ids or the resolver directly.
/// </summary>
public interface IStepExecutionContext
{
    /// <summary>
    /// Module id of the workflow under execution (e.g. from WorkflowDefinition.ModuleId).
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// Resolves the slot to a live client of type T. Returns null if no active connection binding exists for the slot.
    /// Caller must dispose the result when T is IDisposable or IAsyncDisposable.
    /// </summary>
    /// <typeparam name="T">Expected client type (e.g. IDbConnection).</typeparam>
    /// <param name="slotName">Slot name declared by the module.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved client, or null if the slot has no active connection binding.</returns>
    ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default) where T : class;
}
