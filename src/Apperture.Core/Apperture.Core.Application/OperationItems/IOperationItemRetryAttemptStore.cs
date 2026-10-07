namespace Apperture.Core.Application.OperationItems;

/// <summary>SDK-4A — durable store for per-item retry attempts.</summary>
public interface IOperationItemRetryAttemptStore
{
    Task<OperationItemRetryAttempt> CreateAsync(
        CreateOperationItemRetryAttemptRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the attempt, or returns null when the item already has an attempt with this idempotency key, including one a
    /// concurrent request committed first. Unlike <see cref="CreateAsync"/>, it never hands back another request's attempt as
    /// if this one had made it, so a caller that records who requested the attempt can tell the two apart.
    /// </summary>
    Task<OperationItemRetryAttempt?> TryCreateAsync(
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

    Task<OperationItemRetryAttempt?> GetByChildRunIdAsync(
        string childRunId,
        CancellationToken cancellationToken = default);

    /// <summary>SDK-4B — set Succeeded/Failed/Blocked with child item links. No-op if already terminal with same status.</summary>
    Task<OperationItemRetryAttempt> MarkTerminalAsync(
        MarkOperationItemRetryAttemptTerminalRequest request,
        CancellationToken cancellationToken = default);
}
