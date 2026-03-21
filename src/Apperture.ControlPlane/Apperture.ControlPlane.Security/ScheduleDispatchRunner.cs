using System.Text.Json;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Runs one schedule dispatch cycle. Epic 28.
/// </summary>
public sealed class ScheduleDispatchRunner : IScheduleDispatchRunner
{
    private readonly IScheduleEvaluator _evaluator;
    private readonly IScheduleRepository _repo;
    private readonly ISchedulerLifecycleGuard _guard;
    private readonly IWorkflowStartDispatchService _dispatch;
    private readonly IAuditService _audit;

    public ScheduleDispatchRunner(
        IScheduleEvaluator evaluator,
        IScheduleRepository repo,
        ISchedulerLifecycleGuard guard,
        IWorkflowStartDispatchService dispatch,
        IAuditService audit)
    {
        _evaluator = evaluator;
        _repo = repo;
        _guard = guard;
        _dispatch = dispatch;
        _audit = audit;
    }

    public async ValueTask RunCycleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken ct = default)
    {
        var due = await _evaluator.GetDueAsync(planeId, asOfUtc, ct);
        foreach (var candidate in due)
        {
            await ProcessDueAsync(candidate, ct);
        }
    }

    private async Task ProcessDueAsync(ScheduleFireCandidate candidate, CancellationToken ct)
    {
        var claimed = await _repo.TryClaimFireAsync(candidate.ScheduleId, candidate.FireTimeUtc, candidate.AssignmentId, ct);
        if (!claimed)
            return;

        var definition = await _repo.GetDefinitionAsync(new ScheduleId(candidate.ScheduleId), ct);
        if (definition is null)
        {
            await CompleteWithOutcomeAsync(candidate, "Failed", "Schedule definition not found", null, ct);
            return;
        }

        if (definition.Target.JobType != ScheduledJobType.WorkflowExecution)
        {
            await CompleteWithOutcomeAsync(candidate, "Failed", $"Unsupported target type: {definition.Target.JobType}", null, ct);
            return;
        }

        var workflowId = ParseWorkflowDefinitionId(definition.Target.JobConfiguration);
        if (workflowId is null)
        {
            await CompleteWithOutcomeAsync(candidate, "Failed", "Invalid JobConfiguration: missing workflowDefinitionId", null, ct);
            return;
        }

        var canDispatch = await _guard.CanDispatchWorkflowAsync(workflowId, ct);
        if (!canDispatch)
        {
            await CompleteWithOutcomeAsync(candidate, "BlockedByLifecycle", "Module disabled or definition not executable", null, ct);
            try
            {
                await _audit.AppendAsync(new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system:scheduler"),
                    SchedulerAuditActionNames.BlockedByLifecycle,
                    Resource: null,
                    Outcome: "blocked",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                    Details: $"schedule:{candidate.ScheduleId};Workflow {workflowId.Value} not executable (module disabled or unavailable)"
                ), ct);
            }
            catch { /* best-effort audit */ }
            return;
        }

        var request = new StartWorkflowRequest(
            workflowId,
            Parameters: null,
            IdempotencyKey: $"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}",
            CorrelationId: $"schedule:{candidate.ScheduleId}",
            RetryOfRunId: null);

        var result = await _dispatch.StartAsync(request, ct);
        if (result is null)
        {
            await CompleteWithOutcomeAsync(candidate, "Failed", "Dispatch returned null", null, ct);
            try
            {
                await _audit.AppendAsync(new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system:scheduler"),
                    SchedulerAuditActionNames.Failed,
                    Resource: null,
                    Outcome: "failed",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                    Details: $"schedule:{candidate.ScheduleId};Workflow start dispatch returned null"
                ), ct);
            }
            catch { /* best-effort audit */ }
            return;
        }

        await CompleteWithOutcomeAsync(candidate, "Dispatched", null, result.RunId.Value, ct);
        try
        {
            await _audit.AppendAsync(new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("system:scheduler"),
                SchedulerAuditActionNames.Dispatched,
                Resource: null,
                Outcome: "success",
                Timestamp: DateTimeOffset.UtcNow,
                new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                Details: $"schedule:{candidate.ScheduleId};RunId={result.RunId.Value}"
            ), ct);
        }
        catch { /* best-effort audit */ }

        var now = DateTimeOffset.UtcNow;
        await _repo.UpdateStateAsync(candidate.AssignmentId, new ScheduleStateUpdate(
            NextDueUtc: null,
            LastAttemptedUtc: now,
            LastOutcome: "Dispatched",
            LastOutcomeReason: null,
            LastSuccessfulDispatchUtc: now
        ), ct);
    }

    private async Task CompleteWithOutcomeAsync(ScheduleFireCandidate candidate, string outcome, string? reason, string? runId, CancellationToken ct)
    {
        await _repo.CompleteRunAsync(candidate.ScheduleId, candidate.FireTimeUtc, outcome, reason, runId, ct);
        var now = DateTimeOffset.UtcNow;
        await _repo.UpdateStateAsync(candidate.AssignmentId, new ScheduleStateUpdate(
            NextDueUtc: null,
            LastAttemptedUtc: now,
            LastOutcome: outcome,
            LastOutcomeReason: reason,
            LastSuccessfulDispatchUtc: outcome == "Dispatched" ? now : null
        ), ct);
    }

    private static WorkflowDefinitionId? ParseWorkflowDefinitionId(string? jobConfiguration)
    {
        if (string.IsNullOrWhiteSpace(jobConfiguration))
            return null;
        try
        {
            var doc = JsonDocument.Parse(jobConfiguration);
            if (doc.RootElement.TryGetProperty("workflowDefinitionId", out var prop))
            {
                var val = prop.GetString();
                return string.IsNullOrEmpty(val) ? null : new WorkflowDefinitionId(val);
            }
        }
        catch { }
        return null;
    }
}
