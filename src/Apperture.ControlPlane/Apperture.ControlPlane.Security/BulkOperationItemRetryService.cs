using System.Text.Json;
using Apperture.Core.Application;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

public interface IBulkOperationItemRetryService
{
    /// <summary>
    /// Retries selected failed items within one run + one operation group.
    /// Returns null when the run or group is missing / mismatched.
    /// Throws <see cref="UnauthorizedAccessException"/> when denied.
    /// Throws <see cref="OperationItemRetryException"/> for whole-request validation failures.
    /// </summary>
    Task<BulkRetryOperationItemsResult?> BulkRetryAsync(
        OperationRunId runId,
        string operationGroupId,
        BulkRetryOperationItemsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BulkRetryOperationItemsRequest(
    IReadOnlyList<string> OperationItemIds,
    string? RequestId = null,
    string? Reason = null);

public sealed record BulkRetryOperationItemsResult(
    int RequestedCount,
    int DispatchedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<BulkRetryOperationItemResult> Items);

public sealed record BulkRetryOperationItemResult(
    string OperationItemId,
    string? DisplayLabel,
    string Category,
    string Message,
    string? ChildRunId = null,
    string? RetryAttemptId = null);

/// <summary>
/// JOB-1 — bounded selective bulk retry. Reuses <see cref="IOperationItemRetryService"/> per item
/// with independent idempotency keys. Does not cancel in-flight work. Acknowledgement does not block retry.
/// </summary>
public sealed class BulkOperationItemRetryService : IBulkOperationItemRetryService
{
    private readonly AppertureDbContext _db;
    private readonly IOperationItemStore _items;
    private readonly IOperationItemRetryService _itemRetry;
    private readonly IWorkflowRunRetryAuthorizationService _retryAuthz;
    private readonly IPrincipalContext _principal;
    private readonly IAuditService _audit;
    private readonly ILogger<BulkOperationItemRetryService> _logger;

    public BulkOperationItemRetryService(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemRetryService itemRetry,
        IWorkflowRunRetryAuthorizationService retryAuthz,
        IPrincipalContext principal,
        IAuditService audit,
        ILogger<BulkOperationItemRetryService> logger)
    {
        _db = db;
        _items = items;
        _itemRetry = itemRetry;
        _retryAuthz = retryAuthz;
        _principal = principal;
        _audit = audit;
        _logger = logger;
    }

    public async Task<BulkRetryOperationItemsResult?> BulkRetryAsync(
        OperationRunId runId,
        string operationGroupId,
        BulkRetryOperationItemsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(operationGroupId))
            return null;

        var run = await _db.OperationRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
            return null;

        await _retryAuthz.EnsureCanRetryRunAsync(new WorkflowDefinitionId(run.DefinitionId), cancellationToken)
            .ConfigureAwait(false);

        var group = await _items.GetGroupAsync(operationGroupId.Trim(), cancellationToken).ConfigureAwait(false);
        if (group is null)
            return null;
        if (!string.Equals(group.RunId, runId.Value, StringComparison.Ordinal))
            return null;

        var rawIds = request.OperationItemIds ?? Array.Empty<string>();
        var orderedUnique = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in rawIds)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var id = raw.Trim();
            if (seen.Add(id))
                orderedUnique.Add(id);
        }

        if (orderedUnique.Count == 0)
        {
            throw new OperationItemRetryException(
                "empty_selection",
                "At least one operation item id is required.",
                isConflict: false);
        }

        if (orderedUnique.Count > OperationItemAttentionBounds.MaxBulkItemCount)
        {
            throw new OperationItemRetryException(
                "batch_too_large",
                $"Bulk retry is limited to {OperationItemAttentionBounds.MaxBulkItemCount} items per request.",
                isConflict: false);
        }

        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("N")
            : request.RequestId.Trim();
        if (requestId.Length > OperationItemRetryBounds.IdempotencyKeyMaxLength)
        {
            throw new OperationItemRetryException(
                "request_id_too_long",
                $"requestId exceeds {OperationItemRetryBounds.IdempotencyKeyMaxLength} characters.",
                isConflict: false);
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > OperationItemRetryBounds.ReasonMaxLength })
        {
            throw new OperationItemRetryException(
                "reason_too_long",
                $"reason exceeds {OperationItemRetryBounds.ReasonMaxLength} characters.",
                isConflict: false);
        }

        var actor = await _principal.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        var actorId = actor?.Id.Value ?? "unknown";
        var correlation = new CorrelationId($"bulk-retry:{runId.Value}:{requestId}");

        await TryAuditAsync(
            actorId,
            OperationItemResolutionAuditActions.BulkRetryRequested,
            "success",
            correlation,
            runId.Value,
            JsonSerializer.Serialize(new
            {
                runId = runId.Value,
                operationGroupId = group.OperationGroupId,
                requestedCount = orderedUnique.Count,
                requestId,
            }),
            cancellationToken).ConfigureAwait(false);

        var results = new List<BulkRetryOperationItemResult>(orderedUnique.Count);
        var dispatched = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var itemId in orderedUnique)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = await _items.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
            if (item is null)
            {
                // Do not leak inaccessible / foreign ids beyond opaque not-found.
                results.Add(new BulkRetryOperationItemResult(
                    itemId,
                    DisplayLabel: null,
                    BulkRetryOperationItemResultCategories.ItemNotFound,
                    "Item was not found in this operation group.",
                    ChildRunId: null,
                    RetryAttemptId: null));
                skipped++;
                continue;
            }

            if (!string.Equals(item.RunId, runId.Value, StringComparison.Ordinal)
                || !string.Equals(item.OperationGroupId, group.OperationGroupId, StringComparison.Ordinal))
            {
                results.Add(new BulkRetryOperationItemResult(
                    item.OperationItemId,
                    item.DisplayLabel,
                    BulkRetryOperationItemResultCategories.WrongGroup,
                    "Item does not belong to the requested run and operation group.",
                    ChildRunId: null,
                    RetryAttemptId: null));
                skipped++;
                continue;
            }

            if (!string.Equals(item.Status, OperationItemStatuses.Failed, StringComparison.Ordinal))
            {
                results.Add(new BulkRetryOperationItemResult(
                    item.OperationItemId,
                    item.DisplayLabel,
                    BulkRetryOperationItemResultCategories.NotFailed,
                    $"Item is not Failed (status '{item.Status}').",
                    ChildRunId: null,
                    RetryAttemptId: null));
                skipped++;
                continue;
            }

            if (!item.IsRetryable)
            {
                results.Add(new BulkRetryOperationItemResult(
                    item.OperationItemId,
                    item.DisplayLabel,
                    BulkRetryOperationItemResultCategories.NotRetryable,
                    "Item is not marked retryable.",
                    ChildRunId: null,
                    RetryAttemptId: null));
                skipped++;
                continue;
            }

            var perItemKey = BuildPerItemIdempotencyKey(requestId, item.OperationItemId);
            try
            {
                var attempt = await _itemRetry.RequestRetryAsync(
                    runId,
                    item.OperationItemId,
                    new RequestOperationItemRetryDto(perItemKey, reason),
                    cancellationToken).ConfigureAwait(false);

                if (attempt is null)
                {
                    results.Add(new BulkRetryOperationItemResult(
                        item.OperationItemId,
                        item.DisplayLabel,
                        BulkRetryOperationItemResultCategories.ItemNotFound,
                        "Item was not found.",
                        ChildRunId: null,
                        RetryAttemptId: null));
                    skipped++;
                    continue;
                }

                var category = ClassifyAttempt(attempt);
                var message = string.IsNullOrWhiteSpace(attempt.Message)
                    ? DefaultMessage(category)
                    : attempt.Message!;

                results.Add(new BulkRetryOperationItemResult(
                    item.OperationItemId,
                    item.DisplayLabel,
                    category,
                    message,
                    attempt.ChildRunId,
                    attempt.OperationItemRetryAttemptId));

                if (category == BulkRetryOperationItemResultCategories.Dispatched)
                    dispatched++;
                else if (category is BulkRetryOperationItemResultCategories.AlreadyRequested
                         or BulkRetryOperationItemResultCategories.Blocked
                         or BulkRetryOperationItemResultCategories.ModuleUnsupported)
                    skipped++;
                else
                    failed++;
            }
            catch (OperationItemRetryException ex)
            {
                var category = MapErrorCode(ex.ErrorCode);
                results.Add(new BulkRetryOperationItemResult(
                    item.OperationItemId,
                    item.DisplayLabel,
                    category,
                    ex.Message,
                    ChildRunId: null,
                    RetryAttemptId: null));
                if (category is BulkRetryOperationItemResultCategories.DispatchFailed)
                    failed++;
                else
                    skipped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Bulk retry dispatch failed for item {OperationItemId}", item.OperationItemId);
                results.Add(new BulkRetryOperationItemResult(
                    item.OperationItemId,
                    item.DisplayLabel,
                    BulkRetryOperationItemResultCategories.DispatchFailed,
                    "Retry could not be dispatched for this item.",
                    ChildRunId: null,
                    RetryAttemptId: null));
                failed++;
            }
        }

        var aggregate = new BulkRetryOperationItemsResult(
            RequestedCount: orderedUnique.Count,
            DispatchedCount: dispatched,
            SkippedCount: skipped,
            FailedCount: failed,
            Items: results);

        await TryAuditAsync(
            actorId,
            OperationItemResolutionAuditActions.BulkRetryCompleted,
            failed > 0 && dispatched == 0 ? "failure" : "success",
            correlation,
            runId.Value,
            JsonSerializer.Serialize(new
            {
                runId = runId.Value,
                operationGroupId = group.OperationGroupId,
                requestId,
                aggregate.RequestedCount,
                aggregate.DispatchedCount,
                aggregate.SkippedCount,
                aggregate.FailedCount,
            }),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Bulk retry completed for run {RunId} group {GroupId}: requested={Requested} dispatched={Dispatched} skipped={Skipped} failed={Failed}",
            runId.Value,
            group.OperationGroupId,
            aggregate.RequestedCount,
            aggregate.DispatchedCount,
            aggregate.SkippedCount,
            aggregate.FailedCount);

        return aggregate;
    }

    /// <summary>
    /// Deterministic per-item key derived from the batch request id so repeats are idempotent
    /// without sharing one key across items.
    /// </summary>
    public static string BuildPerItemIdempotencyKey(string requestId, string operationItemId)
    {
        var combined = $"bulk:{requestId}:{operationItemId}";
        if (combined.Length <= OperationItemRetryBounds.IdempotencyKeyMaxLength)
            return combined;
        // Truncate safely while keeping item id suffix for uniqueness within the batch.
        var prefixBudget = OperationItemRetryBounds.IdempotencyKeyMaxLength - operationItemId.Length - 1;
        if (prefixBudget < 8)
            return (requestId.Length > 32 ? requestId[..32] : requestId) + ":" + operationItemId;
        var prefix = $"bulk:{requestId}";
        if (prefix.Length > prefixBudget)
            prefix = prefix[..prefixBudget];
        return prefix + ":" + operationItemId;
    }

    private static string ClassifyAttempt(OperationItemRetryAttemptDto attempt)
    {
        if (string.Equals(attempt.Status, OperationItemRetryAttemptStatuses.Dispatched, StringComparison.Ordinal)
            || string.Equals(attempt.Status, OperationItemRetryAttemptStatuses.Succeeded, StringComparison.Ordinal))
        {
            var isReplay = attempt.Message is not null
                && attempt.Message.Contains("Idempotent", StringComparison.OrdinalIgnoreCase);
            return isReplay
                ? BulkRetryOperationItemResultCategories.AlreadyRequested
                : BulkRetryOperationItemResultCategories.Dispatched;
        }

        if (string.Equals(attempt.Status, OperationItemRetryAttemptStatuses.Requested, StringComparison.Ordinal))
            return BulkRetryOperationItemResultCategories.AlreadyRequested;

        if (string.Equals(attempt.Status, OperationItemRetryAttemptStatuses.Blocked, StringComparison.Ordinal))
        {
            if (string.Equals(attempt.ErrorCode, OperationItemRetryErrorCodes.ModuleUnsupported, StringComparison.Ordinal))
                return BulkRetryOperationItemResultCategories.ModuleUnsupported;
            return BulkRetryOperationItemResultCategories.Blocked;
        }

        return BulkRetryOperationItemResultCategories.DispatchFailed;
    }

    private static string MapErrorCode(string errorCode) =>
        errorCode switch
        {
            OperationItemRetryErrorCodes.NotFailed => BulkRetryOperationItemResultCategories.NotFailed,
            OperationItemRetryErrorCodes.NotRetryable => BulkRetryOperationItemResultCategories.NotRetryable,
            OperationItemRetryErrorCodes.ModuleUnsupported => BulkRetryOperationItemResultCategories.ModuleUnsupported,
            _ => BulkRetryOperationItemResultCategories.DispatchFailed,
        };

    private static string DefaultMessage(string category) =>
        category switch
        {
            BulkRetryOperationItemResultCategories.Dispatched => "Retry dispatched.",
            BulkRetryOperationItemResultCategories.AlreadyRequested => "Retry work already existed for this item.",
            BulkRetryOperationItemResultCategories.Blocked => "Retry was blocked.",
            _ => "Retry was not dispatched.",
        };

    private async Task TryAuditAsync(
        string actorId,
        string action,
        string outcome,
        CorrelationId correlation,
        string runId,
        string details,
        CancellationToken cancellationToken)
    {
        try
        {
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId(actorId),
                    action,
                    Resource: null,
                    outcome,
                    DateTimeOffset.UtcNow,
                    correlation,
                    details,
                    ModuleId: null,
                    ScheduleId: null,
                    RunId: runId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append audit for {Action}", action);
        }
    }
}
