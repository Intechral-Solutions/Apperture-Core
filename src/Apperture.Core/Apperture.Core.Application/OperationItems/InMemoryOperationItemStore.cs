using System.Collections.Concurrent;

namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// In-memory store for unit tests and harnesses. Process-local only — not a substitute for SQL durability.
/// </summary>
public sealed class InMemoryOperationItemStore : IOperationItemStore
{
    private readonly ConcurrentDictionary<string, OperationGroup> _groups = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<OperationItem>> _items = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task<OperationGroup> BeginGroupAsync(BeginOperationGroupRequest request, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        OperationItemizationValidation.ValidateBegin(request);

        var now = request.StartedAtUtc ?? DateTimeOffset.UtcNow;
        var group = new OperationGroup(
            OperationGroupId: Guid.NewGuid().ToString("N"),
            RunId: request.RunId.Trim(),
            IntegrationId: request.IntegrationId,
            ModuleId: request.ModuleId.Trim(),
            ProcedureId: request.ProcedureId,
            OperationKind: request.OperationKind.Trim(),
            SourceLabel: request.SourceLabel,
            Status: OperationGroupStatuses.Running,
            TotalCount: 0,
            SucceededCount: 0,
            FailedCount: 0,
            SkippedCount: 0,
            StartedAtUtc: now,
            CompletedAtUtc: null,
            SummaryJson: request.SummaryJson,
            MetadataJson: request.MetadataJson,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        if (!_groups.TryAdd(group.OperationGroupId, group))
            throw new OperationItemizationException("Failed to create operation group.");

        _items[group.OperationGroupId] = [];
        return Task.FromResult(group);
    }

    public Task<OperationItem> AppendItemAsync(AppendOperationItemRequest request, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        OperationItemizationValidation.ValidateAppend(request);

        lock (_gate)
        {
            if (!_groups.TryGetValue(request.OperationGroupId, out var group))
                throw new OperationItemizationException($"Unknown operation group '{request.OperationGroupId}'.");

            if (group.Status != OperationGroupStatuses.Running)
                throw new OperationItemizationException($"Cannot append to group '{request.OperationGroupId}' in status '{group.Status}'.");

            if (!string.Equals(group.RunId, request.RunId, StringComparison.Ordinal))
                throw new OperationItemizationException("AppendItem RunId does not match the group's RunId.");

            var now = DateTimeOffset.UtcNow;
            var item = new OperationItem(
                OperationItemId: Guid.NewGuid().ToString("N"),
                OperationGroupId: request.OperationGroupId,
                RunId: request.RunId.Trim(),
                Sequence: request.Sequence,
                SubjectKey: request.SubjectKey,
                DisplayLabel: request.DisplayLabel,
                Status: request.Status,
                ErrorCode: request.ErrorCode,
                ErrorMessage: request.ErrorMessage,
                IsRetryable: request.IsRetryable,
                RetryCount: request.RetryCount,
                LastAttemptedAtUtc: request.LastAttemptedAtUtc,
                CompletedAtUtc: request.CompletedAtUtc
                               ?? (request.Status == OperationItemStatuses.Pending ? null : now),
                ChangeSummaryJson: request.ChangeSummaryJson,
                MetadataJson: request.MetadataJson,
                CreatedAtUtc: now,
                UpdatedAtUtc: now);

            _items[request.OperationGroupId].Add(item);
            return Task.FromResult(item);
        }
    }

    public Task<OperationGroup> CompleteGroupAsync(CompleteOperationGroupRequest request, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        OperationItemizationValidation.ValidateComplete(request);

        lock (_gate)
        {
            if (!_groups.TryGetValue(request.OperationGroupId, out var group))
                throw new OperationItemizationException($"Unknown operation group '{request.OperationGroupId}'.");

            if (group.Status != OperationGroupStatuses.Running)
                throw new OperationItemizationException($"Group '{request.OperationGroupId}' is already completed.");

            var now = request.CompletedAtUtc ?? DateTimeOffset.UtcNow;
            var completed = group with
            {
                Status = request.Status,
                TotalCount = request.TotalCount,
                SucceededCount = request.SucceededCount,
                FailedCount = request.FailedCount,
                SkippedCount = request.SkippedCount,
                SummaryJson = request.SummaryJson ?? group.SummaryJson,
                CompletedAtUtc = now,
                UpdatedAtUtc = now,
            };

            _groups[request.OperationGroupId] = completed;
            return Task.FromResult(completed);
        }
    }

    public Task<OperationGroup?> GetGroupAsync(string operationGroupId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(
            string.IsNullOrWhiteSpace(operationGroupId) ? null : _groups.GetValueOrDefault(operationGroupId));
    }

    public Task<IReadOnlyList<OperationItem>> GetItemsByGroupAsync(string operationGroupId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<OperationItem>>(
                _items.TryGetValue(operationGroupId, out var list) ? list.ToArray() : []);
        }
    }

    public Task<IReadOnlyList<OperationGroup>> GetGroupsByRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(runId))
            return Task.FromResult<IReadOnlyList<OperationGroup>>([]);

        return Task.FromResult<IReadOnlyList<OperationGroup>>(
            _groups.Values
                .Where(g => string.Equals(g.RunId, runId, StringComparison.Ordinal))
                .OrderBy(g => g.StartedAtUtc)
                .ToArray());
    }
}
