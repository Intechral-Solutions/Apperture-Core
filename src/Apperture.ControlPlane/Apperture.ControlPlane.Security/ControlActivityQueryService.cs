using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Workflow;
using Apperture.Core.Security.Trust;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class ControlActivityQueryService : IControlActivityQueryService
{
    private readonly IRunQueryService _runs;
    private readonly IRuntimeIdentityService _runtimeIdentity;
    private readonly IRuntimeControlService _runtimeControl;
    private readonly IControlActionAvailabilityProjection _actions;
    private readonly IAuthorizationEvaluationService _authorization;
    private readonly IPrincipalContext _principal;
    private readonly IEnvironmentContext _environment;
    private readonly AppertureDbContext _db;

    public ControlActivityQueryService(
        IRunQueryService runs,
        IRuntimeIdentityService runtimeIdentity,
        IRuntimeControlService runtimeControl,
        IControlActionAvailabilityProjection actions,
        IAuthorizationEvaluationService authorization,
        IPrincipalContext principal,
        IEnvironmentContext environment,
        AppertureDbContext db)
    {
        _runs = runs;
        _runtimeIdentity = runtimeIdentity;
        _runtimeControl = runtimeControl;
        _actions = actions;
        _authorization = authorization;
        _principal = principal;
        _environment = environment;
        _db = db;
    }

    public async ValueTask<IReadOnlyList<ControlActivitySummaryDto>> ListRecentActivityAsync(
        DateTimeOffset createdFromUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principal.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Array.Empty<ControlActivitySummaryDto>();

        // Executions are already permission-filtered inside RunQueryService.
        var executionRuns = await _runs.ListRunsAsync(
            new RunQueryFilter(CreatedFrom: createdFromUtc),
            cancellationToken);

        var activities = new List<(DateTimeOffset RequestedAt, ControlActivitySummaryDto Dto)>(executionRuns.Count + 8);

        // Preload queued park/resume flags for workflows to keep action projection consistent and avoid N+1 queries.
        var queuedByRunId = new Dictionary<string, (bool ParkQueued, bool ResumeQueued)>(StringComparer.Ordinal);
        var workflowRunIds = executionRuns
            .Where(r => string.Equals(r.ExecutionKind, "workflow", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.RunId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (workflowRunIds.Length > 0)
        {
            var queuedControls = await _db.WorkflowExternalControlRequests.AsNoTracking()
                .Where(x =>
                    workflowRunIds.Contains(x.RunId) &&
                    x.Status == WorkflowExternalControlStatusNames.Queued &&
                    (x.ControlKind == WorkflowExternalControlKindNames.Park || x.ControlKind == WorkflowExternalControlKindNames.Resume))
                .Select(x => new { x.RunId, x.ControlKind })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var row in queuedControls)
            {
                if (!queuedByRunId.TryGetValue(row.RunId, out var current))
                    current = (false, false);

                if (string.Equals(row.ControlKind, WorkflowExternalControlKindNames.Park, StringComparison.Ordinal))
                    current.ParkQueued = true;
                if (string.Equals(row.ControlKind, WorkflowExternalControlKindNames.Resume, StringComparison.Ordinal))
                    current.ResumeQueued = true;

                queuedByRunId[row.RunId] = current;
            }
        }

        foreach (var run in executionRuns)
        {
            var activitySource = ControlFacade.ActivitySourceDirect;
            string targetKind;
            string targetId;

            if (ControlFacade.TryParseScheduleId(run.CorrelationKey, out var scheduleId))
            {
                activitySource = ControlFacade.ActivitySourceScheduled;
                targetKind = ControlFacade.TargetKindSchedule;
                targetId = scheduleId;
            }
            else
            {
                activitySource = ControlFacade.ActivitySourceDirect;
                targetKind = string.Equals(run.ExecutionKind, "operation", StringComparison.OrdinalIgnoreCase)
                    ? ControlFacade.TargetKindOperation
                    : ControlFacade.TargetKindWorkflow;
                targetId = run.ExecutionDefinitionId;
            }

            var (outcome, summary) = ControlFacade.NormalizeRunOutcome(run.Status, run.ExecutionKind);

            var (hasQueuedPark, hasQueuedResume) = queuedByRunId.TryGetValue(run.RunId, out var flags)
                ? flags
                : (false, false);

            var availableActions = string.Equals(run.ExecutionKind, "workflow", StringComparison.OrdinalIgnoreCase)
                ? await _actions.ProjectExecutionRunActionsAsync(run, hasQueuedPark, hasQueuedResume, cancellationToken).ConfigureAwait(false)
                : await _actions.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false, cancellationToken).ConfigureAwait(false);

            var dto = new ControlActivitySummaryDto(
                ActivityKind: ControlFacade.ActivityKindExecutionRun,
                ActivityId: run.RunId,
                TargetKind: targetKind,
                TargetId: targetId,
                RequestedAtIso: run.CreatedAt.ToString("o"),
                CompletedAtIso: run.CompletedAt?.ToString("o"),
                CurrentNormalizedStatus: ControlFacade.NormalizeRunStatus(run.Status),
                Outcome: outcome,
                OutcomeSummary: summary,
                ActivitySource: activitySource,
                DetailLink: $"/runs/{run.RunId}",
                AvailableActions: availableActions);

            activities.Add((run.CreatedAt, dto));
        }

        // Runtime control requests are persisted separately and must be permission-filtered at facade-level.
        var runtimes = await _runtimeIdentity.GetAllAsync(cancellationToken);
        var envId = _environment.EnvironmentId;

        foreach (var runtime in runtimes)
        {
            if (!await CanReadRuntimeAsync(actor, runtime.Id, envId, cancellationToken).ConfigureAwait(false))
                continue;

            var requests = await _runtimeControl.GetShutdownRequestsAsync(runtime.Id, cancellationToken).ConfigureAwait(false);
            foreach (var r in requests)
            {
                var normalized = r.Status.ToLowerInvariant() switch
                {
                    RuntimeControlStatusNames.Queued => ControlFacade.NormalizedRequested,
                    RuntimeControlStatusNames.Applied => ControlFacade.NormalizedSucceeded,
                    RuntimeControlStatusNames.Rejected => ControlFacade.NormalizedFailed,
                    RuntimeControlStatusNames.Failed => ControlFacade.NormalizedFailed,
                    _ => ControlFacade.NormalizedRequested
                };

                var (outcome, summary) = normalized switch
                {
                    var s when s == ControlFacade.NormalizedSucceeded => ("succeeded", "Runtime shutdown applied."),
                    var s when s == ControlFacade.NormalizedFailed => ("failed", "Runtime shutdown request failed."),
                    var s when s == ControlFacade.NormalizedCancelled => ("cancelled", "Runtime shutdown request cancelled."),
                    _ => ("in_progress", "Runtime shutdown request is queued.")
                };

                var dto = new ControlActivitySummaryDto(
                    ActivityKind: ControlFacade.ActivityKindRuntimeControlRequest,
                    ActivityId: r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    TargetKind: ControlFacade.TargetKindRuntimeInstance,
                    TargetId: runtime.Id,
                    RequestedAtIso: r.RequestedAt.ToString("o"),
                    CompletedAtIso: r.AppliedAt?.ToString("o"),
                    CurrentNormalizedStatus: normalized,
                    Outcome: outcome,
                    OutcomeSummary: summary,
                    ActivitySource: ControlFacade.ActivitySourceDirect,
                    DetailLink: $"/runtimes/{runtime.Id}/controls/shutdown/requests/{r.Id}",
                    AvailableActions: Array.Empty<ControlActionAvailabilityDto>());

                activities.Add((r.RequestedAt, dto));
            }
        }

        // Unified ordering by requested time (runs use CreatedAt; runtime controls use RequestedAt).
        return activities
            .OrderByDescending(a => a.RequestedAt)
            .Take(Math.Max(1, take))
            .Select(a => a.Dto)
            .ToList();
    }

    private async Task<bool> CanReadRuntimeAsync(IActor actor, string runtimeId, string envId, CancellationToken cancellationToken)
    {
        var runtimeScopeKey = ScopeKeyConstants.RuntimeInstance(envId, runtimeId);
        var permission = new PermissionDescriptor(
            ActionConstants.RuntimeRead,
            new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, runtimeScopeKey));

        return await _authorization.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, permission, permission.Resource),
            cancellationToken).ConfigureAwait(false);
    }
}

