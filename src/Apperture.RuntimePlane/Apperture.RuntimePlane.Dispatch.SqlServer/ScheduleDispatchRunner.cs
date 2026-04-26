using System.Text.Json;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.Workflow;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Dispatch.SqlServer;

/// <summary>
/// Runs one schedule dispatch cycle. Epic 28 (workflow), Epic 29 (operation), Epic 36 (multi-host claim/recover).
/// Multi-host coordination audit: only stale recovery appends <see cref="SchedulerAuditActionNames.ClaimRecovered"/>; new claims are not audited.
/// </summary>
public sealed class ScheduleDispatchRunner : IScheduleDispatchRunner
{
    private readonly IScheduleEvaluator _evaluator;
    private readonly IScheduleRepository _repo;
    private readonly ISchedulerLifecycleGuard _guard;
    private readonly IIntegrationRepository _integrations;
    private readonly IWorkflowStartDispatchService _workflowDispatch;
    private readonly IOperationStartDispatchService _operationDispatch;
    private readonly IAuditService _audit;
    private readonly ISchedulerHostIdentity _hostIdentity;
    private readonly ISchedulerClaimOptions _claimOptions;

    public ScheduleDispatchRunner(
        IScheduleEvaluator evaluator,
        IScheduleRepository repo,
        ISchedulerLifecycleGuard guard,
        IIntegrationRepository integrations,
        IWorkflowStartDispatchService workflowDispatch,
        IOperationStartDispatchService operationDispatch,
        IAuditService audit,
        ISchedulerHostIdentity hostIdentity,
        ISchedulerClaimOptions claimOptions)
    {
        _evaluator = evaluator;
        _repo = repo;
        _guard = guard;
        _integrations = integrations;
        _workflowDispatch = workflowDispatch;
        _operationDispatch = operationDispatch;
        _audit = audit;
        _hostIdentity = hostIdentity;
        _claimOptions = claimOptions;
    }

    public async ValueTask RunCycleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken ct = default)
    {
        var hostId = _hostIdentity.HostId;
        var staleCutoffUtc = asOfUtc.AddMinutes(-_claimOptions.ClaimLeaseMinutes);

        var due = (await _evaluator.GetDueAsync(planeId, asOfUtc, ct)).Select(NormalizeFireUtc).ToList();
        var stale = (await _repo.GetRecoverableStaleCandidatesAsync(planeId, staleCutoffUtc, ct)).Select(NormalizeFireUtc).ToList();
        // Normalize so the same UTC instant always matches (due vs stale dedup and unique index on ScheduleRuns).
        var candidates = due
            .Concat(stale.Where(s => !due.Any(d => d.ScheduleId == s.ScheduleId && d.FireTimeUtc == s.FireTimeUtc)))
            .ToList();

        foreach (var candidate in candidates)
        {
            await ProcessDueAsync(candidate, hostId, staleCutoffUtc, ct);
        }
    }

    private static ScheduleFireCandidate NormalizeFireUtc(ScheduleFireCandidate c) =>
        new(c.ScheduleId, c.AssignmentId, new DateTimeOffset(c.FireTimeUtc.UtcDateTime, TimeSpan.Zero));

    private async Task ProcessDueAsync(ScheduleFireCandidate candidate, string hostId, DateTimeOffset staleCutoffUtc, CancellationToken ct)
    {
        var claimed = await _repo.TryClaimFireAsync(candidate.ScheduleId, candidate.FireTimeUtc, candidate.AssignmentId, hostId, ct);
        if (!claimed)
        {
            claimed = await _repo.TryRecoverStaleClaimAsync(candidate.ScheduleId, candidate.FireTimeUtc, candidate.AssignmentId, hostId, staleCutoffUtc, ct);
            if (claimed)
            {
                try
                {
                    await _audit.AppendAsync(new AuditRecord(
                        new AuditRecordId(Guid.NewGuid().ToString("N")),
                        new ActorId("system:scheduler"),
                        SchedulerAuditActionNames.ClaimRecovered,
                        Resource: null,
                        Outcome: "recovered",
                        Timestamp: DateTimeOffset.UtcNow,
                        new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                        Details: $"schedule:{candidate.ScheduleId};fire={candidate.FireTimeUtc:O};recovered by host {hostId}",
                        ScheduleId: candidate.ScheduleId
                    ), ct);
                }
                catch { /* best-effort audit */ }
            }
        }

        if (!claimed)
            return;

        var definition = await _repo.GetDefinitionAsync(new ScheduleId(candidate.ScheduleId), ct);
        if (definition is null)
        {
            await CompleteWithOutcomeAsync(candidate, "Failed", "Schedule definition not found", null, ct);
            return;
        }

        if (definition.Target.JobType == ScheduledJobType.WorkflowExecution)
        {
            await ProcessWorkflowTargetAsync(definition, candidate, ct);
            return;
        }

        if (definition.Target.JobType == ScheduledJobType.ModuleOperationExecution)
        {
            await ProcessOperationTargetAsync(definition, candidate, ct);
            return;
        }

        await CompleteWithOutcomeAsync(candidate, "Failed", $"Unsupported target type: {definition.Target.JobType}", null, ct);
    }

    private async Task ProcessWorkflowTargetAsync(ScheduleDefinition definition, ScheduleFireCandidate candidate, CancellationToken ct)
    {
        if (!await EnsureIntegrationOperationalAsync(definition.IntegrationId, candidate, ct))
            return;

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
                    Details: $"schedule:{candidate.ScheduleId};Workflow {workflowId.Value} not executable (module disabled or unavailable)",
                    ScheduleId: candidate.ScheduleId
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
            RetryOfRunId: null,
            IntegrationId: definition.IntegrationId?.Value,
            ProcedureId: null,
            ExecutionOrigin: RunExecutionOriginNames.Schedule);

        var result = await _workflowDispatch.StartAsync(request, ct);
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
                    Details: $"schedule:{candidate.ScheduleId};Workflow start dispatch returned null",
                    ScheduleId: candidate.ScheduleId
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
                Details: $"schedule:{candidate.ScheduleId};RunId={result.RunId.Value}",
                ScheduleId: candidate.ScheduleId,
                RunId: result.RunId.Value
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

    private async Task ProcessOperationTargetAsync(ScheduleDefinition definition, ScheduleFireCandidate candidate, CancellationToken ct)
    {
        if (!await EnsureIntegrationOperationalAsync(definition.IntegrationId, candidate, ct))
            return;

        var operationId = ParseOperationDefinitionId(definition.Target.JobConfiguration);
        if (operationId is null)
        {
            await CompleteWithOutcomeAsync(candidate, "Failed", "Invalid JobConfiguration: missing operationDefinitionId", null, ct);
            return;
        }

        var canDispatch = await _guard.CanDispatchOperationAsync(operationId, ct);
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
                    Details: $"schedule:{candidate.ScheduleId};Operation {operationId.Value} not executable (module disabled or unavailable)",
                    ScheduleId: candidate.ScheduleId
                ), ct);
            }
            catch { /* best-effort audit */ }
            return;
        }

        var request = new StartOperationRequest(
            operationId.Value,
            Parameters: null,
            IdempotencyKey: $"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}",
            CorrelationId: $"schedule:{candidate.ScheduleId}",
            IntegrationId: definition.IntegrationId?.Value,
            ProcedureId: null,
            ExecutionOrigin: RunExecutionOriginNames.Schedule);

        var result = await _operationDispatch.StartAsync(request, ct);
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
                    Details: $"schedule:{candidate.ScheduleId};Operation start dispatch returned null",
                    ScheduleId: candidate.ScheduleId
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
                Details: $"schedule:{candidate.ScheduleId};RunId={result.RunId.Value}",
                ScheduleId: candidate.ScheduleId,
                RunId: result.RunId.Value
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

    private async Task<bool> EnsureIntegrationOperationalAsync(IntegrationId? integrationId, ScheduleFireCandidate candidate, CancellationToken ct)
    {
        if (integrationId is null)
        {
            try
            {
                await _audit.AppendAsync(new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("system:scheduler"),
                    SchedulerAuditActionNames.LegacyScheduleWithoutIntegration,
                    Resource: null,
                    Outcome: "warning",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId($"schedule:{candidate.ScheduleId}:{candidate.FireTimeUtc:O}"),
                    Details: $"schedule:{candidate.ScheduleId};legacy schedule has no IntegrationId; dispatch is temporarily allowed for backward compatibility.",
                    ScheduleId: candidate.ScheduleId
                ), ct);
            }
            catch
            {
                // best-effort audit
            }
            return true;
        }

        var integration = await _integrations.GetByIdAsync(integrationId, ct);
        if (integration is not null && integration.State == IntegrationState.Active)
            return true;

        var reason = integration is null
            ? $"Integration '{integrationId.Value}' was not found."
            : $"Integration '{integration.Id.Value}' is {integration.State}; schedule dispatch requires Active state.";
        await CompleteWithOutcomeAsync(candidate, "BlockedByLifecycle", reason, null, ct);
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
                Details: $"schedule:{candidate.ScheduleId};{reason}",
                ScheduleId: candidate.ScheduleId
            ), ct);
        }
        catch
        {
            // best-effort audit
        }

        return false;
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

    private static OperationId? ParseOperationDefinitionId(string? jobConfiguration)
    {
        if (string.IsNullOrWhiteSpace(jobConfiguration))
            return null;
        try
        {
            var doc = JsonDocument.Parse(jobConfiguration);
            if (doc.RootElement.TryGetProperty("operationDefinitionId", out var prop))
            {
                var val = prop.GetString();
                return string.IsNullOrEmpty(val) ? null : new OperationId(val);
            }
        }
        catch { }
        return null;
    }
}
