using Apperture.Core.Application.OperationItems;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class OperationItemizationQueryService : IOperationItemizationQueryService
{
    private readonly AppertureDbContext _db;
    private readonly IOperationItemStore _store;
    private readonly IOperationItemAttentionAcknowledgementStore _acks;
    private readonly IOperationItemRetryAttemptStore _attempts;
    private readonly IRunKindResolver _runKinds;
    private readonly IWorkflowRunReadAuthorizationService _workflowReadAuthz;
    private readonly IOperationRunReadAuthorizationService _operationReadAuthz;

    public OperationItemizationQueryService(
        AppertureDbContext db,
        IOperationItemStore store,
        IOperationItemAttentionAcknowledgementStore acks,
        IOperationItemRetryAttemptStore attempts,
        IRunKindResolver runKinds,
        IWorkflowRunReadAuthorizationService workflowReadAuthz,
        IOperationRunReadAuthorizationService operationReadAuthz)
    {
        _db = db;
        _store = store;
        _acks = acks;
        _attempts = attempts;
        _runKinds = runKinds;
        _workflowReadAuthz = workflowReadAuthz;
        _operationReadAuthz = operationReadAuthz;
    }

    public async Task<IReadOnlyList<OperationGroupSummaryDto>?> ListGroupsByRunAsync(
        OperationRunId runId,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureCanReadRunAsync(runId, cancellationToken).ConfigureAwait(false))
            return null;

        var groups = await _store.GetGroupsByRunAsync(runId.Value, cancellationToken).ConfigureAwait(false);
        return groups.Select(MapGroup).ToArray();
    }

    public async Task<OperationItemPageDto?> ListItemsByGroupAsync(
        OperationRunId runId,
        string operationGroupId,
        OperationItemQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationGroupId))
            return null;

        if (!await EnsureCanReadRunAsync(runId, cancellationToken).ConfigureAwait(false))
            return null;

        var group = await _store.GetGroupAsync(operationGroupId.Trim(), cancellationToken).ConfigureAwait(false);
        if (group is null)
            return null;

        if (!string.Equals(group.RunId, runId.Value, StringComparison.Ordinal))
            return null;

        var page = await _store.QueryItemsByGroupAsync(group.OperationGroupId, query, cancellationToken)
            .ConfigureAwait(false);

        var itemIds = page.Items.Select(i => i.OperationItemId).ToArray();
        var activeAcks = await _acks.GetActiveByItemIdsAsync(itemIds, cancellationToken).ConfigureAwait(false);

        var retryFlags = new Dictionary<string, (bool Active, bool Succeeded)>(StringComparer.Ordinal);
        foreach (var id in itemIds)
        {
            var attempts = await _attempts.ListByItemAsync(id, cancellationToken).ConfigureAwait(false);
            var hasActive = attempts.Any(a => OperationItemAttentionRules.IsActiveRetryStatus(a.Status));
            var hasSucceeded = attempts.Any(a => OperationItemAttentionRules.IsSucceededRetryStatus(a.Status));
            retryFlags[id] = (hasActive, hasSucceeded);
        }

        var mapped = page.Items.Select(i =>
        {
            activeAcks.TryGetValue(i.OperationItemId, out var ack);
            retryFlags.TryGetValue(i.OperationItemId, out var flags);
            return MapItem(i, ack, flags.Active, flags.Succeeded);
        }).ToArray();

        return new OperationItemPageDto(mapped, page.TotalCount, page.Skip, page.Take);
    }

    public async Task<RunOperationItemizationSummaryDto?> GetRunSummaryAsync(
        OperationRunId runId,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureCanReadRunAsync(runId, cancellationToken).ConfigureAwait(false))
            return null;

        var summary = await _store.GetRunSummaryAsync(runId.Value, cancellationToken).ConfigureAwait(false);
        var acknowledged = await _acks.CountActiveAcknowledgedFailuresByRunAsync(runId.Value, cancellationToken)
            .ConfigureAwait(false);

        // Failed items without active acknowledgement and without a succeeded retry.
        var failedItems = await _db.OperationItems.AsNoTracking()
            .Where(i => i.RunId == runId.Value && i.Status == OperationItemStatuses.Failed)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activeAcks = await _acks.GetActiveByItemIdsAsync(failedItems, cancellationToken).ConfigureAwait(false);
        var attention = 0;
        foreach (var id in failedItems)
        {
            var hasAck = activeAcks.ContainsKey(id);
            var attempts = await _attempts.ListByItemAsync(id, cancellationToken).ConfigureAwait(false);
            var hasSucceeded = attempts.Any(a => OperationItemAttentionRules.IsSucceededRetryStatus(a.Status));
            if (OperationItemAttentionRules.NeedsAttention(OperationItemStatuses.Failed, hasAck, hasSucceeded))
                attention++;
        }

        return new RunOperationItemizationSummaryDto(
            summary.GroupCount,
            summary.FailedItemCount,
            summary.SkippedItemCount,
            summary.HasOperationItems,
            attention,
            acknowledged);
    }

    /// <summary>
    /// Returns false when the run is missing. Throws <see cref="UnauthorizedAccessException"/> when denied.
    /// </summary>
    private async Task<bool> EnsureCanReadRunAsync(OperationRunId runId, CancellationToken cancellationToken)
    {
        var run = await _db.OperationRuns.AsNoTracking()
            .Where(x => x.Id == runId.Value)
            .Select(x => new { x.Id, x.DefinitionId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
            return false;

        var runKind = await _runKinds.ResolveRunKindAsync(run.Id, cancellationToken).ConfigureAwait(false);
        if (runKind is null)
            return false;

        if (string.Equals(runKind, RunKindNames.Workflow, StringComparison.Ordinal))
            await _workflowReadAuthz.EnsureCanReadRunsAsync(new WorkflowDefinitionId(run.DefinitionId), cancellationToken)
                .ConfigureAwait(false);
        else
            await _operationReadAuthz.EnsureCanReadRunsAsync(new OperationId(run.DefinitionId), cancellationToken)
                .ConfigureAwait(false);

        return true;
    }

    private static OperationGroupSummaryDto MapGroup(OperationGroup g) =>
        new(
            g.OperationGroupId,
            g.RunId,
            g.IntegrationId,
            g.ModuleId,
            g.ProcedureId,
            g.OperationKind,
            g.SourceLabel,
            g.Status,
            g.TotalCount,
            g.SucceededCount,
            g.FailedCount,
            g.SkippedCount,
            g.StartedAtUtc,
            g.CompletedAtUtc,
            g.SummaryJson,
            g.MetadataJson);

    private static OperationItemSummaryDto MapItem(
        OperationItem i,
        OperationItemAttentionAcknowledgement? ack,
        bool hasActiveRetry,
        bool hasSucceededRetry)
    {
        var hasAck = ack is { IsActive: true };
        return new OperationItemSummaryDto(
            i.OperationItemId,
            i.OperationGroupId,
            i.RunId,
            i.Sequence,
            i.SubjectKey,
            i.DisplayLabel,
            i.Status,
            i.ErrorCode,
            i.ErrorMessage,
            i.IsRetryable,
            i.RetryCount,
            i.LastAttemptedAtUtc,
            i.CompletedAtUtc,
            i.ChangeSummaryJson,
            i.MetadataJson,
            i.CreatedAtUtc,
            i.UpdatedAtUtc,
            OperationItemAttentionRules.NeedsAttention(i.Status, hasAck, hasSucceededRetry),
            hasAck && ack is not null ? OperationItemAttentionService.MapAck(ack) : null,
            hasActiveRetry,
            hasSucceededRetry);
    }
}
