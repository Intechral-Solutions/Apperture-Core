using Apperture.Core.Application.Audit;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

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
/// <remarks>
/// Acknowledge and reopen are required mutations (design gate §13.1 W1; assigned to AUD-1.3). Each request is one audited unit
/// of work: every acknowledgement (or reopening) it makes and its one audit row, whose target is the run, commit together or
/// not at all. Items it skips (missing, in another group, not failed, already acknowledged, not acknowledged) change nothing
/// and are reported per item as before; a request that changes no item records nothing. The acknowledging user is the unit's
/// own actor, so the acknowledgement row and the audit row name the same person.
/// </remarks>
public sealed class OperationItemAttentionService : IOperationItemAttentionService
{
    private readonly AppertureDbContext _db;
    private readonly IOperationItemStore _items;
    private readonly IOperationItemAttentionAcknowledgementStore _acks;
    private readonly IWorkflowRunRetryAuthorizationService _retryAuthz;
    private readonly IPrincipalContext _principal;
    private readonly IAuditedUnitOfWork _unit;

    public OperationItemAttentionService(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemAttentionAcknowledgementStore acks,
        IWorkflowRunRetryAuthorizationService retryAuthz,
        IPrincipalContext principal,
        IAuditedUnitOfWork unit)
    {
        _db = db;
        _items = items;
        _acks = acks;
        _retryAuthz = retryAuthz;
        _principal = principal;
        _unit = unit;
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

        await RequireActorAsync("Authenticated actor is required to acknowledge failures.", cancellationToken).ConfigureAwait(false);
        var scope = context.Value;

        return await _unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.OperationItemAttentionAcknowledged, AuditActor.CurrentPrincipal),
            (transaction, ct) => AcknowledgeAttemptAsync(transaction, runId, scope, disposition, reason, orderedUnique, ct),
            (committed, ct) => ReconstructAcknowledgeAsync(committed, runId, scope, orderedUnique, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One attempt: every read the outcome depends on (item, group, status, the active acknowledgement) happens in the
    /// transaction, and every acknowledgement it makes commits with the request's one row or not at all.
    /// </summary>
    private async Task<BulkAcknowledgeOperationItemsResult> AcknowledgeAttemptAsync(
        IAuditedTransaction transaction,
        OperationRunId runId,
        AttentionScope scope,
        string disposition,
        string? reason,
        IReadOnlyList<string> orderedUnique,
        CancellationToken cancellationToken)
    {
        var results = new List<BulkAcknowledgeOperationItemResult>(orderedUnique.Count);
        var acknowledged = new List<string>();

        foreach (var itemId in orderedUnique)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (item, skippedResult) = await ClassifyForAcknowledgeAsync(itemId, runId, scope, cancellationToken).ConfigureAwait(false);
            if (skippedResult is not null)
            {
                results.Add(skippedResult);
                continue;
            }

            var created = await _acks.TryCreateAsync(
                new CreateOperationItemAttentionAcknowledgementRequest(
                    item!.OperationItemId,
                    item.RunId,
                    item.OperationGroupId,
                    disposition,
                    reason,
                    transaction.Actor.RecordedValue),
                cancellationToken).ConfigureAwait(false);
            if (created is null)
            {
                // A concurrent request acknowledged it first: that acknowledgement, and its row, belong to that request.
                var winner = await _acks.GetActiveByItemIdAsync(item.OperationItemId, cancellationToken).ConfigureAwait(false);
                results.Add(AlreadyAcknowledged(item, winner));
                continue;
            }

            // Execution outcome is intentionally untouched.
            results.Add(Acknowledged(item, created));
            acknowledged.Add(item.OperationItemId);
        }

        if (acknowledged.Count == 0)
            transaction.DeclareNoOp();
        else
        {
            await transaction.StageAsync(
                AuditEvents.OperationItemAttentionAcknowledged(
                    runId.Value, scope.GroupId, disposition, reason, orderedUnique.Count, acknowledged, scope.ResourceContextId),
                cancellationToken).ConfigureAwait(false);
        }

        return new BulkAcknowledgeOperationItemsResult(
            orderedUnique.Count,
            acknowledged.Count,
            orderedUnique.Count - acknowledged.Count,
            results);
    }

    /// <summary>
    /// The result of a confirmed commit, from its primary row: the items it lists were acknowledged by this request and are
    /// read back in their current state; every other item was skipped, for the reason that still holds. An item that was
    /// acknowledged elsewhere at commit time and reopened since is still reported as it was then: already acknowledged.
    /// </summary>
    private async Task<BulkAcknowledgeOperationItemsResult> ReconstructAcknowledgeAsync(
        AuditCommittedOperation committed,
        OperationRunId runId,
        AttentionScope scope,
        IReadOnlyList<string> orderedUnique,
        CancellationToken cancellationToken)
    {
        var acknowledgedIds = new HashSet<string>(committed.GetStringList("operationItemIds"), StringComparer.Ordinal);
        var results = new List<BulkAcknowledgeOperationItemResult>(orderedUnique.Count);
        foreach (var itemId in orderedUnique)
        {
            if (acknowledgedIds.Contains(itemId))
            {
                var item = await _items.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
                var latest = (await _acks.ListByItemIdAsync(itemId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                results.Add(new BulkAcknowledgeOperationItemResult(
                    itemId,
                    item?.DisplayLabel,
                    BulkAcknowledgeOperationItemResultCategories.Acknowledged,
                    "Failure acknowledged; original execution outcome remains Failed.",
                    latest is null ? null : MapAck(latest)));
                continue;
            }

            var (eligible, skippedResult) = await ClassifyForAcknowledgeAsync(itemId, runId, scope, cancellationToken).ConfigureAwait(false);
            results.Add(skippedResult ?? AlreadyAcknowledged(eligible!, null));
        }

        return new BulkAcknowledgeOperationItemsResult(
            orderedUnique.Count,
            acknowledgedIds.Count,
            orderedUnique.Count - acknowledgedIds.Count,
            results);
    }

    /// <summary>Either the item, eligible for acknowledgement, or the result that says why it is skipped.</summary>
    private async Task<(OperationItem? Item, BulkAcknowledgeOperationItemResult? Skipped)> ClassifyForAcknowledgeAsync(
        string itemId,
        OperationRunId runId,
        AttentionScope scope,
        CancellationToken cancellationToken)
    {
        var (item, skipped) = await ClassifyMembershipAsync(itemId, runId, scope, cancellationToken).ConfigureAwait(false);
        if (skipped is not null)
            return (null, skipped);

        if (!string.Equals(item!.Status, OperationItemStatuses.Failed, StringComparison.Ordinal))
        {
            return (null, new BulkAcknowledgeOperationItemResult(
                item.OperationItemId, item.DisplayLabel, BulkAcknowledgeOperationItemResultCategories.NotFailed,
                $"Item is not Failed (status '{item.Status}'); acknowledgement applies only to failures."));
        }

        var existing = await _acks.GetActiveByItemIdAsync(item.OperationItemId, cancellationToken).ConfigureAwait(false);
        return existing is null ? (item, null) : (null, AlreadyAcknowledged(item, existing));
    }

    private static BulkAcknowledgeOperationItemResult Acknowledged(OperationItem item, OperationItemAttentionAcknowledgement created) =>
        new(
            item.OperationItemId,
            item.DisplayLabel,
            BulkAcknowledgeOperationItemResultCategories.Acknowledged,
            "Failure acknowledged; original execution outcome remains Failed.",
            MapAck(created));

    private static BulkAcknowledgeOperationItemResult AlreadyAcknowledged(OperationItem item, OperationItemAttentionAcknowledgement? existing) =>
        new(
            item.OperationItemId,
            item.DisplayLabel,
            BulkAcknowledgeOperationItemResultCategories.AlreadyAcknowledged,
            "Item already has an active acknowledgement.",
            existing is null ? null : MapAck(existing));

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

        await RequireActorAsync("Authenticated actor is required to reopen attention.", cancellationToken).ConfigureAwait(false);
        var scope = context.Value;

        return await _unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.OperationItemAttentionReopened, AuditActor.CurrentPrincipal),
            (transaction, ct) => ReopenAttemptAsync(transaction, runId, scope, orderedUnique, ct),
            (committed, ct) => ReconstructReopenAsync(committed, runId, scope, orderedUnique, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One attempt: each reopening applies only to an acknowledgement that is still active, so of two concurrent requests only
    /// one reopens it and only that one records it. The reopenings and the request's one row commit together or not at all.
    /// </summary>
    private async Task<BulkReopenOperationItemsResult> ReopenAttemptAsync(
        IAuditedTransaction transaction,
        OperationRunId runId,
        AttentionScope scope,
        IReadOnlyList<string> orderedUnique,
        CancellationToken cancellationToken)
    {
        var results = new List<BulkAcknowledgeOperationItemResult>(orderedUnique.Count);
        var reopened = new List<string>();

        foreach (var itemId in orderedUnique)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (item, skipped) = await ClassifyMembershipAsync(itemId, runId, scope, cancellationToken).ConfigureAwait(false);
            if (skipped is not null)
            {
                results.Add(skipped);
                continue;
            }

            var updated = await _acks.ReopenActiveAsync(item!.OperationItemId, transaction.Actor.RecordedValue, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (updated is null)
            {
                results.Add(NotAcknowledged(item));
                continue;
            }

            results.Add(Reopened(item, updated));
            reopened.Add(item.OperationItemId);
        }

        if (reopened.Count == 0)
            transaction.DeclareNoOp();
        else
        {
            await transaction.StageAsync(
                AuditEvents.OperationItemAttentionReopened(runId.Value, scope.GroupId, orderedUnique.Count, reopened, scope.ResourceContextId),
                cancellationToken).ConfigureAwait(false);
        }

        return new BulkReopenOperationItemsResult(
            orderedUnique.Count,
            reopened.Count,
            orderedUnique.Count - reopened.Count,
            results);
    }

    /// <summary>
    /// The result of a confirmed commit, from its primary row: the items it lists were reopened by this request and are read
    /// back in their current state; every other item was skipped.
    /// </summary>
    private async Task<BulkReopenOperationItemsResult> ReconstructReopenAsync(
        AuditCommittedOperation committed,
        OperationRunId runId,
        AttentionScope scope,
        IReadOnlyList<string> orderedUnique,
        CancellationToken cancellationToken)
    {
        var reopenedIds = new HashSet<string>(committed.GetStringList("operationItemIds"), StringComparer.Ordinal);
        var results = new List<BulkAcknowledgeOperationItemResult>(orderedUnique.Count);
        foreach (var itemId in orderedUnique)
        {
            var (item, skipped) = await ClassifyMembershipAsync(itemId, runId, scope, cancellationToken).ConfigureAwait(false);
            if (skipped is not null)
            {
                results.Add(skipped);
                continue;
            }

            if (!reopenedIds.Contains(itemId))
            {
                results.Add(NotAcknowledged(item!));
                continue;
            }

            var latestReopened = (await _acks.ListByItemIdAsync(itemId, cancellationToken).ConfigureAwait(false))
                .Where(a => a.ReopenedAtUtc is not null)
                .OrderByDescending(a => a.ReopenedAtUtc)
                .FirstOrDefault();
            results.Add(new BulkAcknowledgeOperationItemResult(
                item!.OperationItemId,
                item.DisplayLabel,
                BulkAcknowledgeOperationItemResultCategories.Reopened,
                "Attention reopened; original execution outcome remains Failed.",
                latestReopened is null ? null : MapAck(latestReopened)));
        }

        return new BulkReopenOperationItemsResult(
            orderedUnique.Count,
            reopenedIds.Count,
            orderedUnique.Count - reopenedIds.Count,
            results);
    }

    private static BulkAcknowledgeOperationItemResult Reopened(OperationItem item, OperationItemAttentionAcknowledgement updated) =>
        new(
            item.OperationItemId,
            item.DisplayLabel,
            BulkAcknowledgeOperationItemResultCategories.Reopened,
            "Attention reopened; original execution outcome remains Failed.",
            MapAck(updated));

    private static BulkAcknowledgeOperationItemResult NotAcknowledged(OperationItem item) =>
        new(
            item.OperationItemId,
            item.DisplayLabel,
            BulkAcknowledgeOperationItemResultCategories.NotAcknowledged,
            "Item does not have an active acknowledgement.");

    /// <summary>Either the item, in the requested run and group, or the result that says why it is skipped.</summary>
    private async Task<(OperationItem? Item, BulkAcknowledgeOperationItemResult? Skipped)> ClassifyMembershipAsync(
        string itemId,
        OperationRunId runId,
        AttentionScope scope,
        CancellationToken cancellationToken)
    {
        var item = await _items.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return (null, new BulkAcknowledgeOperationItemResult(
                itemId, null, BulkAcknowledgeOperationItemResultCategories.ItemNotFound,
                "Item was not found in this operation group."));
        }

        if (!string.Equals(item.RunId, runId.Value, StringComparison.Ordinal)
            || !string.Equals(item.OperationGroupId, scope.GroupId, StringComparison.Ordinal))
        {
            return (null, new BulkAcknowledgeOperationItemResult(
                item.OperationItemId, item.DisplayLabel, BulkAcknowledgeOperationItemResultCategories.WrongGroup,
                "Item does not belong to the requested run and operation group."));
        }

        return (item, null);
    }

    /// <summary>The retry authorization has established an authenticated principal; there is no placeholder to fall back to.</summary>
    private async Task RequireActorAsync(string message, CancellationToken cancellationToken)
    {
        if (await _principal.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false) is null)
            throw new UnauthorizedAccessException(message);
    }

    private readonly record struct AttentionScope(string GroupId, string DefinitionId, string? ResourceContextId);

    private async Task<AttentionScope?> ResolveScopeAsync(
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

        return new AttentionScope(group.OperationGroupId, run.DefinitionId, run.ResourceContextId);
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
}
