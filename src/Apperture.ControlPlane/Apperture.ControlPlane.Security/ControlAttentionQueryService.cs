using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class ControlAttentionQueryService : IControlAttentionQueryService
{
    private readonly IRunQueryService _runs;
    private readonly IAuthorizationEvaluationService _authorization;
    private readonly IPrincipalContext _principal;
    private readonly IEnvironmentContext _environment;
    private readonly AppertureDbContext _db;
    private readonly IWorkflowRunReadAuthorizationService _workflowRunReadAuthz;

    public ControlAttentionQueryService(
        IRunQueryService runs,
        IAuthorizationEvaluationService authorization,
        IPrincipalContext principal,
        IEnvironmentContext environment,
        AppertureDbContext db,
        IWorkflowRunReadAuthorizationService workflowRunReadAuthz)
    {
        _runs = runs;
        _authorization = authorization;
        _principal = principal;
        _environment = environment;
        _db = db;
        _workflowRunReadAuthz = workflowRunReadAuthz;
    }

    public async ValueTask<IReadOnlyList<ControlAttentionItemDto>> ListAttentionAsync(
        DateTimeOffset createdFromUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principal.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Array.Empty<ControlAttentionItemDto>();

        var attention = new List<(DateTimeOffset CreatedAt, ControlAttentionItemDto Dto)>(take);

        // 1) Failed executions (workflow + operations) within the window.
        var failedRuns = await _runs.ListRunsAsync(
            new RunQueryFilter(Status: WorkflowLifecycleStatusNames.Failed, CreatedFrom: createdFromUtc),
            cancellationToken).ConfigureAwait(false);

        foreach (var run in failedRuns)
        {
            var targetKind = ControlFacade.TargetKindWorkflow;
            var targetId = run.ExecutionDefinitionId;

            if (ControlFacade.TryParseScheduleId(run.CorrelationKey, out var scheduleId))
            {
                targetKind = ControlFacade.TargetKindSchedule;
                targetId = scheduleId;
            }
            else if (string.Equals(run.ExecutionKind, "operation", StringComparison.OrdinalIgnoreCase))
            {
                targetKind = ControlFacade.TargetKindOperation;
            }

            attention.Add((run.CreatedAt, new ControlAttentionItemDto(
                AttentionKind: "executionFailed",
                Severity: "high",
                TargetKind: targetKind,
                TargetId: targetId,
                Summary: $"Execution failed (runId={run.RunId}).",
                CreatedAtIso: run.CreatedAt.ToString("o"),
                RelatedActivityId: run.RunId)));
        }

        // 2) Stuck runtime shutdown requests (queued too long).
        var queuedCutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15);
        var shutdownQueued = await _db.RuntimeControlRequests.AsNoTracking()
            .Where(x =>
                x.ControlKind == RuntimeControlKindNames.Shutdown &&
                x.Status == RuntimeControlStatusNames.Queued &&
                x.RequestedAt >= createdFromUtc &&
                x.RequestedAt <= queuedCutoff)
            .Select(x => new { x.RuntimeId, x.Id, x.RequestedAt, x.LastError })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var envId = _environment.EnvironmentId;
        var allowedRuntimeIds = new HashSet<string>(StringComparer.Ordinal);

        // Avoid repeated permission evaluation for runtimes.
        foreach (var runtimeId in shutdownQueued.Select(x => x.RuntimeId).Distinct(StringComparer.Ordinal))
        {
            var runtimeScopeKey = ScopeKeyConstants.RuntimeInstance(envId, runtimeId);
            var permission = new PermissionDescriptor(
                ActionConstants.RuntimeRead,
                new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, runtimeScopeKey));

            var allowed = await _authorization.EvaluateAsync(
                new AuthorizationEvaluationContext(actor, permission, permission.Resource),
                cancellationToken).ConfigureAwait(false);

            if (allowed)
                allowedRuntimeIds.Add(runtimeId);
        }

        foreach (var item in shutdownQueued)
        {
            if (!allowedRuntimeIds.Contains(item.RuntimeId))
                continue;

            var age = DateTimeOffset.UtcNow - item.RequestedAt;
            attention.Add((item.RequestedAt, new ControlAttentionItemDto(
                AttentionKind: "runtimeShutdownQueuedStuck",
                Severity: "high",
                TargetKind: ControlFacade.TargetKindRuntimeInstance,
                TargetId: item.RuntimeId,
                Summary: $"Runtime shutdown is queued for {age.TotalMinutes:0} minutes.",
                CreatedAtIso: item.RequestedAt.ToString("o"),
                RelatedActivityId: item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        // 3) Rejected external control requests for workflow runs.
        var rejectedControls = await _db.WorkflowExternalControlRequests.AsNoTracking()
            .Where(x =>
                x.Status == WorkflowExternalControlStatusNames.Rejected &&
                x.RequestedAt >= createdFromUtc)
            .Select(x => new
            {
                x.Id,
                x.RunId,
                x.WorkflowDefinitionId,
                x.ControlKind,
                x.RequestedAt
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Permission-safe: include only definitions the actor can read.
        var allowedWorkflowDefinitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var defId in rejectedControls.Select(x => x.WorkflowDefinitionId).Distinct(StringComparer.Ordinal))
        {
            try
            {
                await _workflowRunReadAuthz.EnsureCanReadRunsAsync(new WorkflowDefinitionId(defId), cancellationToken).ConfigureAwait(false);
                allowedWorkflowDefinitions.Add(defId);
            }
            catch (UnauthorizedAccessException)
            {
                // Skip definition entirely.
            }
        }

        foreach (var rc in rejectedControls)
        {
            if (!allowedWorkflowDefinitions.Contains(rc.WorkflowDefinitionId))
                continue;

            attention.Add((rc.RequestedAt, new ControlAttentionItemDto(
                AttentionKind: "workflowControlRejected",
                Severity: "warning",
                TargetKind: ControlFacade.TargetKindWorkflow,
                TargetId: rc.WorkflowDefinitionId,
                Summary: $"Workflow external control request rejected (kind={rc.ControlKind}, runId={rc.RunId}).",
                CreatedAtIso: rc.RequestedAt.ToString("o"),
                // Drill-down: open the run detail page for the rejected control.
                RelatedActivityId: rc.RunId)));
        }

        // Unified ordering: newest first.
        return attention
            .OrderByDescending(a => a.CreatedAt)
            .Take(Math.Max(1, take))
            .Select(a => a.Dto)
            .ToList();
    }
}

