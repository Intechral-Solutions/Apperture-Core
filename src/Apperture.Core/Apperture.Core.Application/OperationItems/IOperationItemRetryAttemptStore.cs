namespace Apperture.Core.Application.OperationItems;

/// <summary>SDK-4A — durable store for per-item retry attempts.</summary>
public interface IOperationItemRetryAttemptStore
{
    Task<OperationItemRetryAttempt> CreateAsync(
        CreateOperationItemRetryAttemptRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationItemRetryAttempt?> GetByIdAsync(
        string attemptId,
        CancellationToken cancellationToken = default);

    Task<OperationItemRetryAttempt?> GetByIdempotencyKeyAsync(
        string operationItemId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationItemRetryAttempt>> ListByItemAsync(
        string operationItemId,
        CancellationToken cancellationToken = default);

    Task<int> GetNextAttemptIndexAsync(
        string operationItemId,
        CancellationToken cancellationToken = default);

    Task<OperationItemRetryAttempt> MarkDispatchedAsync(
        MarkOperationItemRetryAttemptDispatchedRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationItemRetryAttempt> MarkBlockedAsync(
        MarkOperationItemRetryAttemptBlockedRequest request,
        CancellationToken cancellationToken = default);
}
