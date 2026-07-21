namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Module-facing append-oriented writer for durable operation itemization.
/// Prefer accessing via <see cref="Execution.IStepExecutionSignals.OperationItems"/>.
/// </summary>
public interface IOperationItemWriter
{
    /// <summary>
    /// Begins a Running group bound to the current execution run context.
    /// </summary>
    ValueTask<OperationGroup> BeginGroupAsync(
        string operationKind,
        string? sourceLabel = null,
        string? summaryJson = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default);

    ValueTask<OperationItem> AppendItemAsync(
        AppendOperationItemRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<OperationGroup> CompleteGroupAsync(
        CompleteOperationGroupRequest request,
        CancellationToken cancellationToken = default);
}
