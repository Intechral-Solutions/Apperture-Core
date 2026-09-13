using System.Collections.Concurrent;

namespace Apperture.Core.Application.OperationItems;

/// <summary>In-memory acknowledgement store for unit tests (JOB-1).</summary>
public sealed class InMemoryOperationItemAttentionAcknowledgementStore : IOperationItemAttentionAcknowledgementStore
{
    private readonly ConcurrentDictionary<string, OperationItemAttentionAcknowledgement> _byId = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task<OperationItemAttentionAcknowledgement> CreateAsync(
        CreateOperationItemAttentionAcknowledgementRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var itemId = request.OperationItemId.Trim();
            var existingActive = _byId.Values.FirstOrDefault(a =>
                string.Equals(a.OperationItemId, itemId, StringComparison.Ordinal) && a.IsActive);
            if (existingActive is not null)
                return Task.FromResult(existingActive);

            var disposition = OperationItemAttentionDispositions.Normalize(request.Disposition)
                ?? throw new OperationItemizationException($"Unknown acknowledgement disposition '{request.Disposition}'.");

            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            if (reason is { Length: > OperationItemAttentionBounds.ReasonMaxLength })
            {
                throw new OperationItemizationException(
                    $"Acknowledgement reason exceeds {OperationItemAttentionBounds.ReasonMaxLength} characters.");
            }

            var now = request.AcknowledgedAtUtc ?? DateTimeOffset.UtcNow;
            var ack = new OperationItemAttentionAcknowledgement(
                AcknowledgementId: Guid.NewGuid().ToString("N"),
                OperationItemId: itemId,
                RunId: request.RunId.Trim(),
                OperationGroupId: request.OperationGroupId.Trim(),
                Disposition: disposition,
                Reason: reason,
                AcknowledgedAtUtc: now,
                AcknowledgedByUserId: request.AcknowledgedByUserId.Trim(),
                ReopenedAtUtc: null,
                ReopenedByUserId: null,
                CreatedAtUtc: now);

            _byId[ack.AcknowledgementId] = ack;
            return Task.FromResult(ack);
        }
    }

    public Task<OperationItemAttentionAcknowledgement?> GetActiveByItemIdAsync(
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(operationItemId))
            return Task.FromResult<OperationItemAttentionAcknowledgement?>(null);

        lock (_gate)
        {
            var active = _byId.Values.FirstOrDefault(a =>
                string.Equals(a.OperationItemId, operationItemId.Trim(), StringComparison.Ordinal) && a.IsActive);
            return Task.FromResult(active);
        }
    }

    public Task<IReadOnlyDictionary<string, OperationItemAttentionAcknowledgement>> GetActiveByItemIdsAsync(
        IReadOnlyCollection<string> operationItemIds,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        ArgumentNullException.ThrowIfNull(operationItemIds);

        lock (_gate)
        {
            var set = new HashSet<string>(
                operationItemIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                StringComparer.Ordinal);
            var map = _byId.Values
                .Where(a => a.IsActive && set.Contains(a.OperationItemId))
                .GroupBy(a => a.OperationItemId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.AcknowledgedAtUtc).First(), StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyDictionary<string, OperationItemAttentionAcknowledgement>>(map);
        }
    }

    public Task<IReadOnlyList<OperationItemAttentionAcknowledgement>> ListByItemIdAsync(
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(operationItemId))
            return Task.FromResult<IReadOnlyList<OperationItemAttentionAcknowledgement>>([]);

        lock (_gate)
        {
            var list = _byId.Values
                .Where(a => string.Equals(a.OperationItemId, operationItemId.Trim(), StringComparison.Ordinal))
                .OrderByDescending(a => a.AcknowledgedAtUtc)
                .ThenByDescending(a => a.CreatedAtUtc)
                .ToArray();
            return Task.FromResult<IReadOnlyList<OperationItemAttentionAcknowledgement>>(list);
        }
    }

    public Task<OperationItemAttentionAcknowledgement?> ReopenActiveAsync(
        string operationItemId,
        string reopenedByUserId,
        DateTimeOffset? reopenedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(operationItemId) || string.IsNullOrWhiteSpace(reopenedByUserId))
            return Task.FromResult<OperationItemAttentionAcknowledgement?>(null);

        lock (_gate)
        {
            var active = _byId.Values.FirstOrDefault(a =>
                string.Equals(a.OperationItemId, operationItemId.Trim(), StringComparison.Ordinal) && a.IsActive);
            if (active is null)
                return Task.FromResult<OperationItemAttentionAcknowledgement?>(null);

            var updated = active with
            {
                ReopenedAtUtc = reopenedAtUtc ?? DateTimeOffset.UtcNow,
                ReopenedByUserId = reopenedByUserId.Trim(),
            };
            _byId[updated.AcknowledgementId] = updated;
            return Task.FromResult<OperationItemAttentionAcknowledgement?>(updated);
        }
    }

    public Task<int> CountActiveAcknowledgedFailuresByRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(runId))
            return Task.FromResult(0);

        lock (_gate)
        {
            return Task.FromResult(_byId.Values.Count(a =>
                a.IsActive && string.Equals(a.RunId, runId.Trim(), StringComparison.Ordinal)));
        }
    }

    public Task<int> CountActiveAcknowledgedFailuresByGroupAsync(
        string operationGroupId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(operationGroupId))
            return Task.FromResult(0);

        lock (_gate)
        {
            return Task.FromResult(_byId.Values.Count(a =>
                a.IsActive
                && string.Equals(a.OperationGroupId, operationGroupId.Trim(), StringComparison.Ordinal)));
        }
    }
}
