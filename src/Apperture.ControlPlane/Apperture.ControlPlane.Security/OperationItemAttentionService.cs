using System.Text.Json;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

public interface IOperationItemAttentionService
{
    /// <summary>
    /// Acknowledges selected failed items within one run + one operation group.
    /// Does not change execution outcome. Does not cancel in-flight retries.
    /// Returns null when the run or group is missing / mismatched.
    /// </summary>
    Task<BulkAcknowledgeOperationItemsResult?> AcknowledgeAsync(
        OperationRunId runId,
        string operationGroupId,
        BulkAcknowledgeOperationItemsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens attention for previously acknowledged items. Preserves acknowledgement history.
    /// </summary>
    Task<BulkReopenOperationItemsResult?> ReopenAttentionAsync(
        OperationRunId runId,
        string operationGroupId,
        BulkReopenOperationItemsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BulkAcknowledgeOperationItemsRequest(
    IReadOnlyList<string> OperationItemIds,
    string Disposition,
    string? Reason = null);

public sealed record BulkAcknowledgeOperationItemsResult(
    int RequestedCount,
    int AcknowledgedCount,
    int SkippedCount,
    IReadOnlyList<BulkAcknowledgeOperationItemResult> Items);

public sealed record BulkAcknowledgeOperationItemResult(
    string OperationItemId,
    string? DisplayLabel,
    string Category,
    string Message,
    OperationItemAcknowledgementDto? Acknowledgement = null);

public sealed record BulkReopenOperationItemsRequest(
    IReadOnlyList<string> OperationItemIds);

public sealed record BulkReopenOperationItemsResult(
    int RequestedCount,
    int ReopenedCount,
    int SkippedCount,
    IReadOnlyList<BulkAcknowledgeOperationItemResult> Items);

public sealed record OperationItemAcknowledgementDto(
    string AcknowledgementId,
    string OperationItemId,
    string Disposition,
    string DispositionLabel,
    string? Reason,
    DateTimeOffset AcknowledgedAtUtc,
    string AcknowledgedByUserId,
    DateTimeOffset? ReopenedAtUtc,
    string? ReopenedByUserId);

public static class BulkAcknowledgeOperationItemResultCategories
{
    public const string Acknowledged = "Acknowledged";
    public const string AlreadyAcknowledged = "AlreadyAcknowledged";
    public const string Reopened = "Reopened";
    public const string NotFailed = "NotFailed";
    public const string NotAcknowledged = "NotAcknowledged";
    public const string ItemNotFound = "ItemNotFound";
    public const string WrongGroup = "WrongGroup";
}

/// <summary>
/// JOB-1 — acknowledgement clears attention without rewriting execution outcome.
/// Race rule: acknowledgement is allowed while a retry is in flight and does not cancel that retry.
/// </summary>
public sealed class OperationItemAttentionService : IOperationItemAttentionService
{
    private readonly AppertureDbContext _db;
    private readonly IOperationItemStore _items;
    private readonly IOperationItemAttentionAcknowledgementStore _acks;
    private readonly IWorkflowRunRetryAuthorizationService _retryAuthz;
    private readonly IPrincipalContext _principal;
    private readonly IAuditService _audit;
    private readonly ILogger<OperationItemAttentionService> _logger;

    public OperationItemAttentionService(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemAttentionAcknowledgementStore acks,
        IWorkflowRunRetryAuthorizationService retryAuthz,
        IPrincipalContext principal,
        IAuditService audit,
        ILogger<OperationItemAttentionService> logger)
    {
        _db = db;
        _items = items;
        _acks = acks;
        _retryAuthz = retryAuthz;
        _principal = principal;
        _audit = audit;
        _logger = logger;
    }

    public async Task<BulkAcknowledgeOperationItemsResult?> AcknowledgeAsync(
        OperationRunId runId,
        string operationGroupId,
        BulkAcknowledgeOperationItemsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await ResolveScopeAsync(runId, operationGroupId, cancellationToken).ConfigureAwait(false);
        if (context is null)
            return null;

        var disposition = OperationItemAttentionDispositions.Normalize(request.Disposition);
        if (disposition is null)
        {
            throw new OperationItemRetryException(
                "invalid_disposition",
                "Disposition must be ResolvedExternally, NoActionRequired, or WillNotRetry.",
                isConflict: false);
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > OperationItemAttentionBounds.ReasonMaxLength })
        {
            throw new OperationItemRetryException(
                "reason_too_long",
                $"reason exceeds {OperationItemAttentionBounds.ReasonMaxLength} characters.",
                isConflict: false);
        }

        var orderedUnique = NormalizeIds(request.OperationItemIds);
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
                $"Acknowledgement is limited to {OperationItemAttentionBounds.MaxBulkItemCount} items per request.",
                isConflict: false);
        }

        var actor = await _principal.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        var actorId = actor?.Id.Value
            ?? throw new UnauthorizedAccessException("Authenticated actor is required to acknowledge failures.");

        var results = new List<BulkAcknowledgeOperationItemResult>(orderedUnique.Count);
        var acknowledged = 0;
        var skipped = 0;

        foreach (var itemId in orderedUnique)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await _items.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
            if (item is null)
            {
                results.Add(new BulkAcknowledgeOperationItemResult(
                    itemId, null, BulkAcknowledgeOperationItemResultCategories.ItemNotFound,
                    "Item was not found in this operation group."));
                skipped++;
                continue;
            }

            if (!string.Equals(item.RunId, runId.Value, StringComparison.Ordinal)
                || !string.Equals(item.OperationGroupId, context.Value.GroupId, StringComparison.Ordinal))
            {
                results.Add(new BulkAcknowledgeOperationItemResult(
                    item.OperationItemId, item.DisplayLabel, BulkAcknowledgeOperationItemResultCategories.WrongGroup,
                    "Item does not belong to the requested run and operation group."));
                skipped++;
                continue;
            }

            if (!string.Equals(item.Status, OperationItemStatuses.Failed, StringComparison.Ordinal))
            {
                results.Add(new BulkAcknowledgeOperationItemResult(
                    item.OperationItemId, item.DisplayLabel, BulkAcknowledgeOperationItemResultCategories.NotFailed,
                    $"Item is not Failed (status '{item.Status}'); acknowledgement applies only to failures."));
                skipped++;
                continue;
            }

            var existing = await _acks.GetActiveByItemIdAsync(item.OperationItemId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                results.Add(new BulkAcknowledgeOperationItemResult(
                    item.OperationItemId,
                    item.DisplayLabel,
                    BulkAcknowledgeOperationItemResultCategories.AlreadyAcknowledged,
                    "Item already has an active acknowledgement.",
                    MapAck(existing)));
                skipped++;
                continue;
            }

            var created = await _acks.CreateAsync(
                new CreateOperationItemAttentionAcknowledgementRequest(
                    item.OperationItemId,
                    item.RunId,
                    item.OperationGroupId,
                    disposition,
                    reason,
                    actorId),
                cancellationToken).ConfigureAwait(false);

            // Execution outcome is intentionally untouched.
            results.Add(new BulkAcknowledgeOperationItemResult(
                item.OperationItemId,
                item.DisplayLabel,
                BulkAcknowledgeOperationItemResultCategories.Acknowledged,
                "Failure acknowledged; original execution outcome remains Failed.",
                MapAck(created)));
            acknowledged++;
        }

        await TryAuditAsync(
            actorId,
            OperationItemResolutionAuditActions.AttentionAcknowledged,
            "success",
            new CorrelationId($"ack:{runId.Value}:{Guid.NewGuid():N}"),
            runId.Value,
            JsonSerializer.Serialize(new
            {
                runId = runId.Value,
                operationGroupId = context.Value.GroupId,
                disposition,
                reason,
                requestedCount = orderedUnique.Count,
                acknowledgedCount = acknowledged,
                skippedCount = skipped,
            }),
            cancellationToken).ConfigureAwait(false);

        return new BulkAcknowledgeOperationItemsResult(
            orderedUnique.Count,
            acknowledged,
            skipped,
            results);
    }

    public async Task<BulkReopenOperationItemsResult?> ReopenAttentionAsync(
        OperationRunId runId,
        string operationGroupId,
        BulkReopenOperationItemsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await ResolveScopeAsync(runId, operationGroupId, cancellationToken).ConfigureAwait(false);
        if (context is null)
            return null;

        var orderedUnique = NormalizeIds(request.OperationItemIds);
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
                $"Reopen attention is limited to {OperationItemAttentionBounds.MaxBulkItemCount} items per request.",
                isConflict: false);
        }

        var actor = await _principal.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        var actorId = actor?.Id.Value
            ?? throw new UnauthorizedAccessException("Authenticated actor is required to reopen attention.");

        var results = new List<BulkAcknowledgeOperationItemResult>(orderedUnique.Count);
        var reopened = 0;
        var skipped = 0;

        foreach (var itemId in orderedUnique)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await _items.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
            if (item is null)
            {
                results.Add(new BulkAcknowledgeOperationItemResult(
                    itemId, null, BulkAcknowledgeOperationItemResultCategories.ItemNotFound,
                    "Item was not found in this operation group."));
                skipped++;
                continue;
            }

            if (!string.Equals(item.RunId, runId.Value, StringComparison.Ordinal)
                || !string.Equals(item.OperationGroupId, context.Value.GroupId, StringComparison.Ordinal))
            {
                results.Add(new BulkAcknowledgeOperationItemResult(
                    item.OperationItemId, item.DisplayLabel, BulkAcknowledgeOperationItemResultCategories.WrongGroup,
                    "Item does not belong to the requested run and operation group."));
                skipped++;
                continue;
            }

            var updated = await _acks.ReopenActiveAsync(item.OperationItemId, actorId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (updated is null)
            {
                results.Add(new BulkAcknowledgeOperationItemResult(
                    item.OperationItemId, item.DisplayLabel, BulkAcknowledgeOperationItemResultCategories.NotAcknowledged,
                    "Item does not have an active acknowledgement."));
                skipped++;
                continue;
            }

            results.Add(new BulkAcknowledgeOperationItemResult(
                item.OperationItemId,
                item.DisplayLabel,
                BulkAcknowledgeOperationItemResultCategories.Reopened,
                "Attention reopened; original execution outcome remains Failed.",
                MapAck(updated)));
            reopened++;
        }

        await TryAuditAsync(
            actorId,
            OperationItemResolutionAuditActions.AttentionReopened,
            "success",
            new CorrelationId($"reopen:{runId.Value}:{Guid.NewGuid():N}"),
            runId.Value,
            JsonSerializer.Serialize(new
            {
                runId = runId.Value,
                operationGroupId = context.Value.GroupId,
                requestedCount = orderedUnique.Count,
                reopenedCount = reopened,
                skippedCount = skipped,
            }),
            cancellationToken).ConfigureAwait(false);

        return new BulkReopenOperationItemsResult(
            orderedUnique.Count,
            reopened,
            skipped,
            results);
    }

    private async Task<(string GroupId, string DefinitionId)?> ResolveScopeAsync(
        OperationRunId runId,
        string operationGroupId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operationGroupId))
            return null;

        var run = await _db.OperationRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
            return null;

        // Same permission as retry — changing attention / recovery disposition is a management action.
        await _retryAuthz.EnsureCanRetryRunAsync(new WorkflowDefinitionId(run.DefinitionId), cancellationToken)
            .ConfigureAwait(false);

        var group = await _items.GetGroupAsync(operationGroupId.Trim(), cancellationToken).ConfigureAwait(false);
        if (group is null)
            return null;
        if (!string.Equals(group.RunId, runId.Value, StringComparison.Ordinal))
            return null;

        return (group.OperationGroupId, run.DefinitionId);
    }

    private static List<string> NormalizeIds(IReadOnlyList<string>? ids)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in ids ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var id = raw.Trim();
            if (seen.Add(id))
                ordered.Add(id);
        }

        return ordered;
    }

    internal static OperationItemAcknowledgementDto MapAck(OperationItemAttentionAcknowledgement a) =>
        new(
            a.AcknowledgementId,
            a.OperationItemId,
            a.Disposition,
            OperationItemAttentionDispositions.ToOperatorLabel(a.Disposition),
            a.Reason,
            a.AcknowledgedAtUtc,
            a.AcknowledgedByUserId,
            a.ReopenedAtUtc,
            a.ReopenedByUserId);

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
