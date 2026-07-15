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
    private readonly IRunKindResolver _runKinds;
    private readonly IWorkflowRunReadAuthorizationService _workflowReadAuthz;
    private readonly IOperationRunReadAuthorizationService _operationReadAuthz;

    public OperationItemizationQueryService(
        AppertureDbContext db,
        IOperationItemStore store,
        IRunKindResolver runKinds,
        IWorkflowRunReadAuthorizationService workflowReadAuthz,
        IOperationRunReadAuthorizationService operationReadAuthz)
    {
        _db = db;
        _store = store;
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

        return new OperationItemPageDto(
            page.Items.Select(MapItem).ToArray(),
            page.TotalCount,
            page.Skip,
            page.Take);
    }

    public async Task<RunOperationItemizationSummaryDto?> GetRunSummaryAsync(
        OperationRunId runId,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureCanReadRunAsync(runId, cancellationToken).ConfigureAwait(false))
            return null;

        var summary = await _store.GetRunSummaryAsync(runId.Value, cancellationToken).ConfigureAwait(false);
        return new RunOperationItemizationSummaryDto(
            summary.GroupCount,
            summary.FailedItemCount,
            summary.SkippedItemCount,
            summary.HasOperationItems);
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

    private static OperationItemSummaryDto MapItem(OperationItem i) =>
        new(
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
            i.UpdatedAtUtc);
}
