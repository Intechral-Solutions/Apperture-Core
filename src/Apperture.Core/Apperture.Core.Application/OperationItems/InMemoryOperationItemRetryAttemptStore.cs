using System.Collections.Concurrent;

namespace Apperture.Core.Application.OperationItems;

/// <summary>In-memory retry attempt store for unit tests (SDK-4A).</summary>
public sealed class InMemoryOperationItemRetryAttemptStore : IOperationItemRetryAttemptStore
{
    private readonly ConcurrentDictionary<string, OperationItemRetryAttempt> _byId = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task<OperationItemRetryAttempt> CreateAsync(
        CreateOperationItemRetryAttemptRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            return Task.FromResult(FindByKeyLocked(request) ?? CreateLocked(request));
        }
    }

    public Task<OperationItemRetryAttempt?> TryCreateAsync(
        CreateOperationItemRetryAttemptRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            return Task.FromResult<OperationItemRetryAttempt?>(FindByKeyLocked(request) is null ? CreateLocked(request) : null);
        }
    }

    private OperationItemRetryAttempt? FindByKeyLocked(CreateOperationItemRetryAttemptRequest request) =>
        _byId.Values.FirstOrDefault(a =>
            string.Equals(a.OperationItemId, request.OperationItemId, StringComparison.Ordinal)
            && string.Equals(a.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal));

    private OperationItemRetryAttempt CreateLocked(CreateOperationItemRetryAttemptRequest request)
    {
        if (_byId.Values.Any(a =>
                string.Equals(a.OperationItemId, request.OperationItemId, StringComparison.Ordinal)
                && a.AttemptIndex == request.AttemptIndex))
        {
            throw new OperationItemizationException(
                $"AttemptIndex {request.AttemptIndex} already exists for item '{request.OperationItemId}'.");
        }

        var now = request.StartedAtUtc ?? DateTimeOffset.UtcNow;
        var attempt = new OperationItemRetryAttempt(
            OperationItemRetryAttemptId: Guid.NewGuid().ToString("N"),
            OperationItemId: request.OperationItemId.Trim(),
            RunId: request.RunId.Trim(),
            OperationGroupId: request.OperationGroupId.Trim(),
            AttemptIndex: request.AttemptIndex,
            IdempotencyKey: request.IdempotencyKey.Trim(),
            RequestedByActorId: request.RequestedByActorId,
            Reason: request.Reason,
            Status: request.Status.Trim(),
            ChildRunId: request.ChildRunId,
            ChildOperationGroupId: request.ChildOperationGroupId,
            ChildOperationItemId: request.ChildOperationItemId,
            StartedAtUtc: now,
            CompletedAtUtc: request.Status is OperationItemRetryAttemptStatuses.Blocked
                or OperationItemRetryAttemptStatuses.Failed
                or OperationItemRetryAttemptStatuses.Succeeded
                ? now
                : null,
            ErrorCode: request.ErrorCode,
            ErrorMessage: request.ErrorMessage,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        _byId[attempt.OperationItemRetryAttemptId] = attempt;
        return attempt;
    }

    public Task<OperationItemRetryAttempt?> GetByIdAsync(
        string attemptId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(
            string.IsNullOrWhiteSpace(attemptId) ? null : _byId.GetValueOrDefault(attemptId));
    }

    public Task<OperationItemRetryAttempt?> GetByIdempotencyKeyAsync(
        string operationItemId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            return Task.FromResult(_byId.Values.FirstOrDefault(a =>
                string.Equals(a.OperationItemId, operationItemId, StringComparison.Ordinal)
                && string.Equals(a.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)));
        }
    }

    public Task<IReadOnlyList<OperationItemRetryAttempt>> ListByItemAsync(
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<OperationItemRetryAttempt>>(
                _byId.Values
                    .Where(a => string.Equals(a.OperationItemId, operationItemId, StringComparison.Ordinal))
                    .OrderBy(a => a.AttemptIndex)
                    .ToArray());
        }
    }

    public Task<int> GetNextAttemptIndexAsync(
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var max = _byId.Values
                .Where(a => string.Equals(a.OperationItemId, operationItemId, StringComparison.Ordinal))
                .Select(a => a.AttemptIndex)
                .DefaultIfEmpty(0)
                .Max();
            return Task.FromResult(max + 1);
        }
    }

    public Task<OperationItemRetryAttempt> MarkDispatchedAsync(
        MarkOperationItemRetryAttemptDispatchedRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            if (!_byId.TryGetValue(request.OperationItemRetryAttemptId, out var current))
                throw new OperationItemizationException($"Unknown retry attempt '{request.OperationItemRetryAttemptId}'.");

            var now = request.DispatchedAtUtc ?? DateTimeOffset.UtcNow;
            var updated = current with
            {
                Status = OperationItemRetryAttemptStatuses.Dispatched,
                ChildRunId = request.ChildRunId.Trim(),
                UpdatedAtUtc = now,
            };
            _byId[updated.OperationItemRetryAttemptId] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task<OperationItemRetryAttempt> MarkBlockedAsync(
        MarkOperationItemRetryAttemptBlockedRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            if (!_byId.TryGetValue(request.OperationItemRetryAttemptId, out var current))
                throw new OperationItemizationException($"Unknown retry attempt '{request.OperationItemRetryAttemptId}'.");

            var now = request.CompletedAtUtc ?? DateTimeOffset.UtcNow;
            var updated = current with
            {
                Status = OperationItemRetryAttemptStatuses.Blocked,
                ErrorCode = request.ErrorCode,
                ErrorMessage = request.ErrorMessage,
                CompletedAtUtc = now,
                UpdatedAtUtc = now,
            };
            _byId[updated.OperationItemRetryAttemptId] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task<OperationItemRetryAttempt?> GetByChildRunIdAsync(
        string childRunId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(childRunId))
            return Task.FromResult<OperationItemRetryAttempt?>(null);

        lock (_gate)
        {
            return Task.FromResult(_byId.Values.FirstOrDefault(a =>
                string.Equals(a.ChildRunId, childRunId, StringComparison.Ordinal)));
        }
    }

    public Task<OperationItemRetryAttempt> MarkTerminalAsync(
        MarkOperationItemRetryAttemptTerminalRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            if (!_byId.TryGetValue(request.OperationItemRetryAttemptId, out var current))
                throw new OperationItemizationException($"Unknown retry attempt '{request.OperationItemRetryAttemptId}'.");

            if (current.Status is OperationItemRetryAttemptStatuses.Succeeded
                or OperationItemRetryAttemptStatuses.Failed
                or OperationItemRetryAttemptStatuses.Blocked)
            {
                return Task.FromResult(current);
            }

            var status = request.Status.Trim();
            if (status is not (OperationItemRetryAttemptStatuses.Succeeded
                or OperationItemRetryAttemptStatuses.Failed
                or OperationItemRetryAttemptStatuses.Blocked))
            {
                throw new OperationItemizationException(
                    $"Terminal status must be Succeeded, Failed, or Blocked (got '{request.Status}').");
            }

            var now = request.CompletedAtUtc ?? DateTimeOffset.UtcNow;
            var updated = current with
            {
                Status = status,
                ChildOperationGroupId = request.ChildOperationGroupId ?? current.ChildOperationGroupId,
                ChildOperationItemId = request.ChildOperationItemId ?? current.ChildOperationItemId,
                ErrorCode = request.ErrorCode,
                ErrorMessage = request.ErrorMessage,
                CompletedAtUtc = now,
                UpdatedAtUtc = now,
            };
            _byId[updated.OperationItemRetryAttemptId] = updated;
            return Task.FromResult(updated);
        }
    }
}
