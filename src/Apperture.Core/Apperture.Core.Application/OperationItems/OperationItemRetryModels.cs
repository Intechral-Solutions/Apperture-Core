namespace Apperture.Core.Application.OperationItems;

/// <summary>SDK-4A — durable append-only retry attempt for a failed operation item.</summary>
public sealed record OperationItemRetryAttempt(
    string OperationItemRetryAttemptId,
    string OperationItemId,
    string RunId,
    string OperationGroupId,
    int AttemptIndex,
    string IdempotencyKey,
    string? RequestedByActorId,
    string? Reason,
    string Status,
    string? ChildRunId,
    string? ChildOperationGroupId,
    string? ChildOperationItemId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateOperationItemRetryAttemptRequest(
    string OperationItemId,
    string RunId,
    string OperationGroupId,
    int AttemptIndex,
    string IdempotencyKey,
    string? RequestedByActorId,
    string? Reason,
    string Status,
    string? ChildRunId = null,
    string? ChildOperationGroupId = null,
    string? ChildOperationItemId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    DateTimeOffset? StartedAtUtc = null);

public sealed record MarkOperationItemRetryAttemptDispatchedRequest(
    string OperationItemRetryAttemptId,
    string ChildRunId,
    DateTimeOffset? DispatchedAtUtc = null);

public sealed record MarkOperationItemRetryAttemptBlockedRequest(
    string OperationItemRetryAttemptId,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset? CompletedAtUtc = null);

/// <summary>SDK-4B — terminal outcome for a dispatched retry attempt.</summary>
public sealed record MarkOperationItemRetryAttemptTerminalRequest(
    string OperationItemRetryAttemptId,
    string Status,
    string? ChildOperationGroupId = null,
    string? ChildOperationItemId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    DateTimeOffset? CompletedAtUtc = null);
