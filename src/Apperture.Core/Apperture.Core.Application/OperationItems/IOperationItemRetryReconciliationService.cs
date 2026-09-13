namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// SDK-4B — reconciles <see cref="OperationItemRetryAttemptStatuses.Dispatched"/> attempts
/// against child run / operation item outcomes. Safe to call repeatedly (idempotent).
/// </summary>
public interface IOperationItemRetryReconciliationService
{
    Task<OperationItemRetryAttempt?> ReconcileAttemptAsync(
        string attemptId,
        CancellationToken cancellationToken = default);

    Task<OperationItemRetryAttempt?> ReconcileByChildRunAsync(
        string childRunId,
        CancellationToken cancellationToken = default);

    /// <summary>Reconciles all non-terminal attempts for a parent operation item.</summary>
    Task<IReadOnlyList<OperationItemRetryAttempt>> ReconcileForParentItemAsync(
        string operationItemId,
        CancellationToken cancellationToken = default);
}
