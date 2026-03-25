using System;
using System.Collections.Generic;
using System.Linq;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class RunTimelineQueryService : IRunTimelineQueryService
{
    private readonly AppertureDbContext _db;
    private readonly IRunKindResolver _runKinds;
    private readonly IWorkflowRunReadAuthorizationService _workflowReadAuthz;
    private readonly IOperationRunReadAuthorizationService _operationReadAuthz;

    public RunTimelineQueryService(
        AppertureDbContext db,
        IRunKindResolver runKinds,
        IWorkflowRunReadAuthorizationService workflowReadAuthz,
        IOperationRunReadAuthorizationService operationReadAuthz)
    {
        _db = db;
        _runKinds = runKinds;
        _workflowReadAuthz = workflowReadAuthz;
        _operationReadAuthz = operationReadAuthz;
    }

    public async Task<RunTimelineDto?> GetRunTimelineAsync(OperationRunId runId, CancellationToken cancellationToken = default)
    {
        var run = await _db.OperationRuns.AsNoTracking()
            .Where(x => x.Id == runId.Value)
            .Select(x => new
            {
                x.Id,
                x.DefinitionId,
                x.CreatedAt,
                x.CompletedAt,
                x.RetryOfRunId
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
            return null;

        var runKind = await _runKinds.ResolveRunKindAsync(run.Id, cancellationToken);
        if (runKind is null)
            return null;
        var isWorkflow = string.Equals(runKind, RunKindNames.Workflow, StringComparison.Ordinal);

        var workflowStart = isWorkflow
            ? await _db.WorkflowStartRequests.AsNoTracking().FirstOrDefaultAsync(x => x.RunId == run.Id, cancellationToken)
            : null;
        var operationStart = isWorkflow
            ? null
            : await _db.OperationStartRequests.AsNoTracking().FirstOrDefaultAsync(x => x.RunId == run.Id, cancellationToken);

        if (isWorkflow)
            await _workflowReadAuthz.EnsureCanReadRunsAsync(new WorkflowDefinitionId(run.DefinitionId), cancellationToken);
        else
            await _operationReadAuthz.EnsureCanReadRunsAsync(new OperationId(run.DefinitionId), cancellationToken);

        var events = new List<RunTimelineEventDto>(16);

        if (isWorkflow && workflowStart is not null)
        {
            events.Add(MakeEvent(
                eventId: $"startRequest:{workflowStart.RunId}:requested",
                runId: run.Id,
                occurredAt: workflowStart.CreatedAt,
                eventKind: RunTimeline.EventKindRunRequested,
                summary: "Run requested.",
                actor: null,
                sourceType: RunTimeline.SourceTypeStartRequest,
                sourceId: workflowStart.RunId,
                metadata: new Dictionary<string, string?>
                {
                    ["workflowDefinitionId"] = workflowStart.WorkflowDefinitionId,
                    ["idempotencyKey"] = workflowStart.IdempotencyKey,
                    ["correlationId"] = workflowStart.CorrelationId
                }));

            if (workflowStart.ClaimedAt is not null)
            {
                events.Add(MakeEvent(
                    eventId: $"startRequest:{workflowStart.RunId}:claimed",
                    runId: run.Id,
                    occurredAt: workflowStart.ClaimedAt.Value,
                    eventKind: RunTimeline.EventKindRunClaimed,
                    summary: "Run claimed for execution.",
                    actor: workflowStart.ClaimedBy,
                    sourceType: RunTimeline.SourceTypeStartRequest,
                    sourceId: workflowStart.RunId,
                    metadata: new Dictionary<string, string?>()));
            }

            var lifecycle = await _db.WorkflowLifecycleEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id)
                .OrderBy(x => x.OccurredAt).ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var item in lifecycle)
            {
                events.Add(MakeEvent(
                    eventId: $"lifecycle:{item.Id}",
                    runId: run.Id,
                    occurredAt: item.OccurredAt,
                    eventKind: RunTimeline.EventKindLifecycleTransition,
                    summary: $"Lifecycle transition: {item.EventType}.",
                    actor: null,
                    sourceType: RunTimeline.SourceTypeLifecycleEvent,
                    sourceId: item.Id.ToString(),
                    metadata: new Dictionary<string, string?>
                    {
                        ["eventType"] = item.EventType,
                        ["fromStatus"] = item.FromStatus,
                        ["toStatus"] = item.ToStatus,
                        ["stepName"] = item.StepName
                    }));
            }

            var controls = await _db.WorkflowExternalControlRequests.AsNoTracking()
                .Where(x => x.RunId == run.Id)
                .OrderBy(x => x.RequestedAt).ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var control in controls)
            {
                events.Add(MakeEvent(
                    eventId: $"control:{control.Id}:requested",
                    runId: run.Id,
                    occurredAt: control.RequestedAt,
                    eventKind: RunTimeline.EventKindExternalControlRequested,
                    summary: $"External control requested: {control.ControlKind}.",
                    actor: control.RequestedBy,
                    sourceType: RunTimeline.SourceTypeExternalControlRequest,
                    sourceId: control.Id.ToString(),
                    metadata: new Dictionary<string, string?>
                    {
                        ["controlKind"] = control.ControlKind,
                        ["status"] = control.Status
                    }));

                if (control.AppliedAt is not null)
                {
                    var resolvedKind = string.Equals(control.Status, WorkflowExternalControlStatusNames.Rejected, StringComparison.Ordinal)
                        ? RunTimeline.EventKindExternalControlRejected
                        : RunTimeline.EventKindExternalControlApplied;

                    events.Add(MakeEvent(
                        eventId: $"control:{control.Id}:resolved",
                        runId: run.Id,
                        occurredAt: control.AppliedAt.Value,
                        eventKind: resolvedKind,
                        summary: $"External control {control.Status}: {control.ControlKind}.",
                        actor: control.AppliedBy,
                        sourceType: RunTimeline.SourceTypeExternalControlRequest,
                        sourceId: control.Id.ToString(),
                        metadata: new Dictionary<string, string?>
                        {
                            ["controlKind"] = control.ControlKind,
                            ["status"] = control.Status,
                            ["lastError"] = control.LastError
                        }));
                }
            }
        }
        else if (operationStart is not null)
        {
            events.Add(MakeEvent(
                eventId: $"startRequest:{operationStart.RunId}:requested",
                runId: run.Id,
                occurredAt: operationStart.CreatedAt,
                eventKind: RunTimeline.EventKindRunRequested,
                summary: "Run requested.",
                actor: null,
                sourceType: RunTimeline.SourceTypeStartRequest,
                sourceId: operationStart.RunId,
                metadata: new Dictionary<string, string?>
                {
                    ["operationDefinitionId"] = operationStart.OperationDefinitionId,
                    ["idempotencyKey"] = operationStart.IdempotencyKey,
                    ["correlationId"] = operationStart.CorrelationId
                }));

            if (operationStart.ClaimedAt is not null)
            {
                events.Add(MakeEvent(
                    eventId: $"startRequest:{operationStart.RunId}:claimed",
                    runId: run.Id,
                    occurredAt: operationStart.ClaimedAt.Value,
                    eventKind: RunTimeline.EventKindRunClaimed,
                    summary: "Run claimed for execution.",
                    actor: operationStart.ClaimedBy,
                    sourceType: RunTimeline.SourceTypeStartRequest,
                    sourceId: operationStart.RunId,
                    metadata: new Dictionary<string, string?>()));
            }

            var lifecycle = await _db.OperationLifecycleEvents.AsNoTracking()
                .Where(x => x.RunId == run.Id)
                .OrderBy(x => x.OccurredAt).ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var item in lifecycle)
            {
                events.Add(MakeEvent(
                    eventId: $"lifecycle:{item.Id}",
                    runId: run.Id,
                    occurredAt: item.OccurredAt,
                    eventKind: RunTimeline.EventKindLifecycleTransition,
                    summary: $"Lifecycle transition: {item.EventType}.",
                    actor: null,
                    sourceType: RunTimeline.SourceTypeLifecycleEvent,
                    sourceId: item.Id.ToString(),
                    metadata: new Dictionary<string, string?>
                    {
                        ["eventType"] = item.EventType,
                        ["fromStatus"] = item.FromStatus,
                        ["toStatus"] = item.ToStatus,
                        ["stepName"] = item.StepName
                    }));
            }
        }

        if (!string.IsNullOrWhiteSpace(run.RetryOfRunId))
        {
            events.Add(MakeEvent(
                eventId: $"run:{run.Id}:retry-link",
                runId: run.Id,
                occurredAt: run.CreatedAt,
                eventKind: RunTimeline.EventKindRetryLinked,
                summary: "Run created as retry of a previous run.",
                actor: null,
                sourceType: RunTimeline.SourceTypeRun,
                sourceId: run.Id,
                metadata: new Dictionary<string, string?> { ["retryOfRunId"] = run.RetryOfRunId }));
        }

        if (run.CompletedAt is not null)
        {
            var hasLifecycleNearCompletion = events.Any(x =>
                string.Equals(x.EventKind, RunTimeline.EventKindLifecycleTransition, StringComparison.Ordinal) &&
                x.OccurredAt == run.CompletedAt.Value);

            if (!hasLifecycleNearCompletion)
            {
                events.Add(MakeEvent(
                    eventId: $"run:{run.Id}:completed-boundary",
                    runId: run.Id,
                    occurredAt: run.CompletedAt.Value,
                    eventKind: RunTimeline.EventKindRunCompletedBoundary,
                    summary: "Run completion boundary persisted.",
                    actor: null,
                    sourceType: RunTimeline.SourceTypeRun,
                    sourceId: run.Id,
                    metadata: new Dictionary<string, string?>()));
            }
        }

        var ordered = events
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.EventId, StringComparer.Ordinal)
            .ToArray();

        return new RunTimelineDto(
            RunId: run.Id,
            ExecutionKind: isWorkflow ? RunKindNames.Workflow : RunKindNames.Operation,
            Events: ordered);
    }

    private static RunTimelineEventDto MakeEvent(
        string eventId,
        string runId,
        DateTimeOffset occurredAt,
        string eventKind,
        string summary,
        string? actor,
        string sourceType,
        string? sourceId,
        IReadOnlyDictionary<string, string?> metadata)
    {
        return new RunTimelineEventDto(
            EventId: eventId,
            RunId: runId,
            OccurredAt: occurredAt,
            EventKind: eventKind,
            Summary: summary,
            Actor: actor,
            SourceType: sourceType,
            SourceId: sourceId,
            Metadata: metadata);
    }
}
