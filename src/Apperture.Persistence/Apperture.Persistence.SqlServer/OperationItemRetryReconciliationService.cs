using System.Text.Json;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SDK-4B — reconciles dispatched retry attempts against child run / operation item outcomes.
/// Explicit + idempotent; safe for Worker post-completion and Management lazy read.
/// </summary>
public sealed class OperationItemRetryReconciliationService : IOperationItemRetryReconciliationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppertureDbContext _db;
    private readonly IOperationItemStore _items;
    private readonly IOperationItemRetryAttemptStore _attempts;

    public OperationItemRetryReconciliationService(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemRetryAttemptStore attempts)
    {
        _db = db;
        _items = items;
        _attempts = attempts;
    }

    public async Task<OperationItemRetryAttempt?> ReconcileAttemptAsync(
        string attemptId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
            return null;

        var attempt = await _attempts.GetByIdAsync(attemptId.Trim(), cancellationToken).ConfigureAwait(false);
        return attempt is null ? null : await ReconcileCoreAsync(attempt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationItemRetryAttempt?> ReconcileByChildRunAsync(
        string childRunId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(childRunId))
            return null;

        var attempt = await _attempts.GetByChildRunIdAsync(childRunId.Trim(), cancellationToken).ConfigureAwait(false);
        return attempt is null ? null : await ReconcileCoreAsync(attempt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OperationItemRetryAttempt>> ReconcileForParentItemAsync(
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationItemId))
            return [];

        var list = await _attempts.ListByItemAsync(operationItemId.Trim(), cancellationToken).ConfigureAwait(false);
        var results = new List<OperationItemRetryAttempt>(list.Count);
        foreach (var attempt in list)
            results.Add(await ReconcileCoreAsync(attempt, cancellationToken).ConfigureAwait(false));
        return results;
    }

    private async Task<OperationItemRetryAttempt> ReconcileCoreAsync(
        OperationItemRetryAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.Status is OperationItemRetryAttemptStatuses.Succeeded
            or OperationItemRetryAttemptStatuses.Failed
            or OperationItemRetryAttemptStatuses.Blocked)
        {
            return attempt;
        }

        // Only Dispatched (and rare lingering Requested) are reconcilable.
        if (attempt.Status is not (OperationItemRetryAttemptStatuses.Dispatched
            or OperationItemRetryAttemptStatuses.Requested))
        {
            return attempt;
        }

        if (string.IsNullOrWhiteSpace(attempt.ChildRunId))
            return attempt;

        var childRun = await _db.OperationRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == attempt.ChildRunId, cancellationToken)
            .ConfigureAwait(false);

        if (childRun is null)
        {
            return await _attempts.MarkTerminalAsync(
                new MarkOperationItemRetryAttemptTerminalRequest(
                    attempt.OperationItemRetryAttemptId,
                    OperationItemRetryAttemptStatuses.Failed,
                    ErrorCode: OperationItemRetryErrorCodes.ChildRunMissing,
                    ErrorMessage: "Child run referenced by the retry attempt was not found."),
                cancellationToken).ConfigureAwait(false);
        }

        if (IsActiveRunStatus(childRun.Status))
            return attempt;

        if (string.Equals(childRun.Status, WorkflowLifecycleStatusNames.Cancelled, StringComparison.Ordinal))
        {
            return await _attempts.MarkTerminalAsync(
                new MarkOperationItemRetryAttemptTerminalRequest(
                    attempt.OperationItemRetryAttemptId,
                    OperationItemRetryAttemptStatuses.Failed,
                    ErrorCode: OperationItemRetryErrorCodes.ChildRunCancelled,
                    ErrorMessage: "Child retry run was cancelled before producing a terminal item outcome.",
                    CompletedAtUtc: childRun.CompletedAt),
                cancellationToken).ConfigureAwait(false);
        }

        var groups = await _items.GetGroupsByRunAsync(attempt.ChildRunId, cancellationToken).ConfigureAwait(false);
        var linkedItems = new List<(OperationGroup Group, OperationItem Item)>();
        foreach (var group in groups)
        {
            var items = await _items.GetItemsByGroupAsync(group.OperationGroupId, cancellationToken).ConfigureAwait(false);
            foreach (var item in items)
                linkedItems.Add((group, item));
        }

        var matched = FindMatchingChildItem(attempt, linkedItems);
        if (matched is null)
        {
            if (linkedItems.Count == 0)
            {
                var (code, message) = string.Equals(
                        childRun.Status,
                        WorkflowLifecycleStatusNames.Failed,
                        StringComparison.Ordinal)
                    ? (OperationItemRetryErrorCodes.ChildRunFailedBeforeItemization,
                        "Child retry run failed before writing operation items.")
                    : (OperationItemRetryErrorCodes.MissingChildOutcome,
                        "Child retry run completed without a reconcilable operation item outcome.");

                return await _attempts.MarkTerminalAsync(
                    new MarkOperationItemRetryAttemptTerminalRequest(
                        attempt.OperationItemRetryAttemptId,
                        OperationItemRetryAttemptStatuses.Failed,
                        ErrorCode: code,
                        ErrorMessage: message,
                        CompletedAtUtc: childRun.CompletedAt),
                    cancellationToken).ConfigureAwait(false);
            }

            return await _attempts.MarkTerminalAsync(
                new MarkOperationItemRetryAttemptTerminalRequest(
                    attempt.OperationItemRetryAttemptId,
                    OperationItemRetryAttemptStatuses.Failed,
                    ChildOperationGroupId: linkedItems[0].Group.OperationGroupId,
                    ErrorCode: OperationItemRetryErrorCodes.AmbiguousChildOutcome,
                    ErrorMessage: "Child run has operation items but none uniquely match this retry attempt.",
                    CompletedAtUtc: childRun.CompletedAt),
                cancellationToken).ConfigureAwait(false);
        }

        var (childGroup, childItem) = matched.Value;
        return await MapChildItemToTerminalAsync(attempt, childGroup, childItem, childRun.CompletedAt, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OperationItemRetryAttempt> MapChildItemToTerminalAsync(
        OperationItemRetryAttempt attempt,
        OperationGroup childGroup,
        OperationItem childItem,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken)
    {
        if (string.Equals(childItem.Status, OperationItemStatuses.Succeeded, StringComparison.Ordinal))
        {
            return await _attempts.MarkTerminalAsync(
                new MarkOperationItemRetryAttemptTerminalRequest(
                    attempt.OperationItemRetryAttemptId,
                    OperationItemRetryAttemptStatuses.Succeeded,
                    ChildOperationGroupId: childGroup.OperationGroupId,
                    ChildOperationItemId: childItem.OperationItemId,
                    CompletedAtUtc: completedAt ?? childItem.CompletedAtUtc),
                cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(childItem.Status, OperationItemStatuses.Failed, StringComparison.Ordinal))
        {
            return await _attempts.MarkTerminalAsync(
                new MarkOperationItemRetryAttemptTerminalRequest(
                    attempt.OperationItemRetryAttemptId,
                    OperationItemRetryAttemptStatuses.Failed,
                    ChildOperationGroupId: childGroup.OperationGroupId,
                    ChildOperationItemId: childItem.OperationItemId,
                    ErrorCode: childItem.ErrorCode,
                    ErrorMessage: childItem.ErrorMessage ?? "Child retry operation item failed.",
                    CompletedAtUtc: completedAt ?? childItem.CompletedAtUtc),
                cancellationToken).ConfigureAwait(false);
        }

        // Skipped (and any other non-success) → Failed with clear code; do not fake success.
        return await _attempts.MarkTerminalAsync(
            new MarkOperationItemRetryAttemptTerminalRequest(
                attempt.OperationItemRetryAttemptId,
                OperationItemRetryAttemptStatuses.Failed,
                ChildOperationGroupId: childGroup.OperationGroupId,
                ChildOperationItemId: childItem.OperationItemId,
                ErrorCode: childItem.ErrorCode ?? OperationItemRetryErrorCodes.ChildItemSkipped,
                ErrorMessage: childItem.ErrorMessage
                              ?? $"Child retry operation item ended as '{childItem.Status}'.",
                CompletedAtUtc: completedAt ?? childItem.CompletedAtUtc),
            cancellationToken).ConfigureAwait(false);
    }

    private static (OperationGroup Group, OperationItem Item)? FindMatchingChildItem(
        OperationItemRetryAttempt attempt,
        IReadOnlyList<(OperationGroup Group, OperationItem Item)> linkedItems)
    {
        if (linkedItems.Count == 0)
            return null;

        foreach (var pair in linkedItems)
        {
            if (TryGetRetryAttemptId(pair.Item.MetadataJson, out var metaAttemptId)
                && string.Equals(metaAttemptId, attempt.OperationItemRetryAttemptId, StringComparison.Ordinal))
            {
                return pair;
            }
        }

        // FileImport retry mode writes exactly one item; accept unambiguous single-item child runs.
        if (linkedItems.Count == 1)
            return linkedItems[0];

        return null;
    }

    public static bool TryGetRetryAttemptId(string? metadataJson, out string attemptId)
    {
        attemptId = "";
        if (string.IsNullOrWhiteSpace(metadataJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("retryAttemptId", out var el)
                || el.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(el.GetString()))
            {
                return false;
            }

            attemptId = el.GetString()!.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsActiveRunStatus(string status) =>
        string.Equals(status, WorkflowLifecycleStatusNames.Requested, StringComparison.Ordinal)
        || string.Equals(status, WorkflowLifecycleStatusNames.Running, StringComparison.Ordinal)
        || string.Equals(status, WorkflowLifecycleStatusNames.Parked, StringComparison.Ordinal);
}
