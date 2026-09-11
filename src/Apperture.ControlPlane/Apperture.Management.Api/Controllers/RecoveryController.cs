using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Environment;
using Apperture.ControlPlane.Security;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Operator recovery and remediation workflows. Epic 31.
/// </summary>
[ApiController]
[Route("api/control/recovery")]
[Authorize]
public sealed class RecoveryController : ControllerBase
{
    private readonly IRecoveryQueryService _recovery;
    private readonly IWorkflowRetryService _retry;
    private readonly IScheduleDispatchRunner _scheduleRunner;
    private readonly IDevWorkerRuntimeRecoveryService _devWorkerRecovery;
    private readonly IRecoveryGovernanceService _recoveryGovernance;
    private readonly IAuditService? _audit;
    private readonly IPrincipalContext _principal;

    public RecoveryController(
        IRecoveryQueryService recovery,
        IWorkflowRetryService retry,
        IScheduleDispatchRunner scheduleRunner,
        IDevWorkerRuntimeRecoveryService devWorkerRecovery,
        IRecoveryGovernanceService recoveryGovernance,
        IPrincipalContext principal,
        IAuditService? audit = null)
    {
        _recovery = recovery;
        _retry = retry;
        _scheduleRunner = scheduleRunner;
        _devWorkerRecovery = devWorkerRecovery;
        _recoveryGovernance = recoveryGovernance;
        _principal = principal;
        _audit = audit;
    }

    /// <summary>
    /// Lists blocked deferred work with remediation eligibility.
    /// NAV-1 stabilization note (independent-review F-08): the standalone Blocked Work page that
    /// called this list endpoint and <see cref="RetryBlockedWork"/> was folded into the Runs
    /// "blocked" status facet, which has no list-level retry action of its own. Kept undeleted
    /// rather than removed -- it is a documented API contract (docs/04-api/contracts.md) with no
    /// internal-only carve-out, and its eligibility reasons / inspection-only guidance for
    /// operation-kind blocked work are richer than the surviving <c>runs/{id}/retry</c> path.
    /// <see cref="RecoveryController.GetBlockedWork"/> (single-item) still has a UI caller
    /// (run detail); this list endpoint and the retry endpoint currently do not. Candidate for a
    /// future execution-domain pass to restore as UI rather than remove.
    /// </summary>
    [HttpGet("blocked-work")]
    [ProducesResponseType(typeof(IReadOnlyList<BlockedWorkRemediationEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<BlockedWorkRemediationEntry>>> ListBlockedWork(
        [FromQuery] string? runtimePlaneId = "local",
        [FromQuery] int windowHours = 24,
        CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanReadAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();
        var list = await _recovery.GetBlockedWorkRemediationAsync(
            runtimePlaneId,
            Math.Max(1, Math.Min(windowHours, 168)),
            cancellationToken).ConfigureAwait(false);
        return Ok(list);
    }

    /// <summary>
    /// Gets a single blocked item's remediation context.
    /// </summary>
    [HttpGet("blocked-work/{runId}")]
    [ProducesResponseType(typeof(BlockedWorkRemediationEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BlockedWorkRemediationEntry>> GetBlockedWork(string runId, CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanReadAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();
        var entry = await _recovery.GetBlockedWorkRemediationForRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return NotFound();
        return Ok(entry);
    }

    /// <summary>
    /// Retries a blocked workflow start. Returns 409 if still ineligible.
    /// Operation blocked work is inspectable only; whole-operation retry is intentionally unsupported (JOB-1 final decision).
    /// </summary>
    [HttpPost("blocked-work/{runId}/retry")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RetryBlockedWork(string runId, CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            await TryAuditRetryDeniedAsync(runId, cancellationToken).ConfigureAwait(false);
            return Forbid();
        }
        var entry = await _recovery.GetBlockedWorkRemediationForRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return NotFound();

        if (string.Equals(entry.DefinitionKind, "operation", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error = "Operation-level retry is not supported.",
                runId,
                definitionKind = "operation",
                inspectionOnly = true,
                finalDecision = true,
                message =
                    "Operation blocked work is inspectable only. Recover by opening the run’s Affected records, then retry or acknowledge individual failed items. Whole-operation retry is not offered because it would re-run successful work already completed in the same operation.",
                recoveryGuidance = new
                {
                    openAffectedRecords = true,
                    itemRetrySupported = true,
                    acknowledgementSupported = true,
                    operationRetrySupported = false,
                },
            });
        }

        if (!entry.RetryEligibleNow)
        {
            await TryAuditRetryRefusedAsync(runId, entry.DefinitionId, entry.RetryEligibleReason, cancellationToken).ConfigureAwait(false);
            return Conflict(new { error = entry.RetryEligibleReason ?? "Retry not eligible.", blocked = true, runId });
        }

        await TryAuditRetryRequestedAsync(runId, entry.DefinitionId, cancellationToken).ConfigureAwait(false);
        var runIdTyped = new OperationRunId(runId);
        try
        {
            var result = await _retry.RequestRetryAsync(runIdTyped, cancellationToken).ConfigureAwait(false);
            if (result is null)
                return NotFound();

            if (result.IsBlocked)
            {
                await TryAuditRetryRefusedAsync(runId, result.OriginalRunId.Value, result.BlockedReason, cancellationToken).ConfigureAwait(false);
                return Conflict(new { error = result.BlockedReason ?? "Retry blocked.", blocked = true, runId });
            }

            await TryAuditRetryDispatchedAsync(runId, result.RunId!.Value, entry.DefinitionId, cancellationToken).ConfigureAwait(false);
            return Accepted(new
            {
                originalRunId = result.OriginalRunId.Value,
                runId = result.RunId.Value,
                workflowInstanceId = result.WorkflowInstanceId!.Value,
                workflowDefinitionId = result.WorkflowDefinitionId.Value,
                status = result.Status
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets schedule remediation context.
    /// </summary>
    [HttpGet("schedules/{id}")]
    [ProducesResponseType(typeof(ScheduleRemediationEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScheduleRemediationEntry>> GetScheduleRemediation(
        string id,
        [FromQuery] string? runtimePlaneId = "local",
        CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanReadAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();
        var entry = await _recovery.GetScheduleRemediationAsync(id, runtimePlaneId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return NotFound();
        return Ok(entry);
    }

    /// <summary>
    /// Triggers a global scheduler re-evaluation cycle for the runtime plane.
    /// Processes all schedules due at the current time; this is not a schedule-specific replay.
    /// SWM-8: returns enriched counts; does <strong>not</strong> write the WorkerHost scheduler heartbeat.
    /// Prefer <c>POST /api/control/scheduler/run-cycle</c> for the clearer alias.
    /// </summary>
    [HttpPost("schedules/run-cycle")]
    [ProducesResponseType(typeof(SchedulerManualOperationResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SchedulerManualOperationResult>> RunScheduleCycle(
        [FromQuery] string? runtimePlaneId = "local",
        CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            await TryAuditRunCycleDeniedAsync(cancellationToken).ConfigureAwait(false);
            return Forbid();
        }

        var planeId = new RuntimePlaneId(runtimePlaneId ?? ScheduleRuntimePlaneDefaults.ExpectedManagementPlaneId);
        string? error = null;
        ScheduleDispatchCycleResult cycle;
        try
        {
            cycle = await _scheduleRunner.RunCycleAsync(planeId, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = SchedulerStatusClassifier.SanitizeErrorSummary(ex);
            cycle = new ScheduleDispatchCycleResult(0, 0, 0, HostId: null);
        }

        var result = SchedulerManualOperationMapper.FromCycle(
            planeId.Value,
            SchedulerManualOperationMapper.ActionRunCycle,
            cycle,
            error);
        await TryAuditRunCycleRequestedAsync(planeId.Value, cancellationToken).ConfigureAwait(false);
        return Accepted(result);
    }

    /// <summary>
    /// SWM-9a: Epic72B file/PID worker status is quarantined.
    /// Use GET /api/control/scheduler-status for supported WorkerHost observability.
    /// </summary>
    [HttpGet("runtime-worker/status")]
    [ProducesResponseType(typeof(DevWorkerRuntimeStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DevWorkerRuntimeStatus>> GetRuntimeWorkerStatus(CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanReadAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();
        var status = await _devWorkerRecovery.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return Ok(status);
    }

    /// <summary>
    /// SWM-9a: WorkerHost start/restart via Management is quarantined (410 Gone).
    /// Does not shell to scripts. Use Aspire AppHost or Manage.ps1 for process lifecycle.
    /// </summary>
    [HttpPost("runtime-worker/start")]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> StartRuntimeWorker([FromQuery] bool restart = false, CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();

        // Still call the service so quarantine messaging stays centralized; never returns success in v1.
        var result = await _devWorkerRecovery.StartAsync(restart, cancellationToken).ConfigureAwait(false);
        return StatusCode(StatusCodes.Status410Gone, new
        {
            error = WorkerLifecycleQuarantine.ErrorCode,
            quarantined = true,
            action = restart ? "runtime_worker_restart" : "runtime_worker_start",
            result.Message,
            observe = "GET /api/control/scheduler-status",
            schedulerOps = new[]
            {
                "POST /api/control/scheduler/run-cycle",
                "POST /api/control/scheduler/recover-stale"
            },
            hostGuidance = new[]
            {
                WorkerLifecycleQuarantine.GuidanceAspire,
                WorkerLifecycleQuarantine.GuidanceScriptDev,
                WorkerLifecycleQuarantine.GuidanceFuture
            }
        });
    }

    /// <summary>
    /// Gets module remediation context.
    /// </summary>
    [HttpGet("modules/{moduleId}")]
    [ProducesResponseType(typeof(ModuleRemediationEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ModuleRemediationEntry>> GetModuleRemediation(
        string moduleId,
        CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanReadAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();
        var entry = await _recovery.GetModuleRemediationAsync(moduleId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return NotFound();
        return Ok(entry);
    }

    private async ValueTask TryAuditRetryRequestedAsync(string runId, string definitionId, CancellationToken ct)
    {
        if (_audit is null) return;
        try
        {
            var actor = await _principal.GetCurrentActorAsync(ct).ConfigureAwait(false);
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    actor?.Id ?? new ActorId("system"),
                    RecoveryAuditActionNames.BlockedWorkRetryRequested,
                    Resource: null,
                    Outcome: "requested",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(runId),
                    $"runId={runId};definitionId={definitionId}",
                    RunId: runId),
                ct).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
    }

    private async ValueTask TryAuditRetryRefusedAsync(string runId, string definitionId, string? reason, CancellationToken ct)
    {
        if (_audit is null) return;
        try
        {
            var actor = await _principal.GetCurrentActorAsync(ct).ConfigureAwait(false);
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    actor?.Id ?? new ActorId("system"),
                    RecoveryAuditActionNames.BlockedWorkRetryRefused,
                    Resource: null,
                    Outcome: "refused",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(runId),
                    $"runId={runId};definitionId={definitionId};reason={reason ?? "unknown"}",
                    RunId: runId),
                ct).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
    }

    private async ValueTask TryAuditRetryDispatchedAsync(string originalRunId, string newRunId, string definitionId, CancellationToken ct)
    {
        if (_audit is null) return;
        try
        {
            var actor = await _principal.GetCurrentActorAsync(ct).ConfigureAwait(false);
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    actor?.Id ?? new ActorId("system"),
                    RecoveryAuditActionNames.BlockedWorkRetryDispatched,
                    Resource: null,
                    Outcome: "dispatched",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(originalRunId),
                    $"originalRunId={originalRunId};newRunId={newRunId};definitionId={definitionId}",
                    RunId: originalRunId),
                ct).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
    }

    private async ValueTask TryAuditRetryDeniedAsync(string runId, CancellationToken ct)
    {
        if (_audit is null) return;
        try
        {
            var actor = await _principal.GetCurrentActorAsync(ct).ConfigureAwait(false);
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    actor?.Id ?? new ActorId("system"),
                    RecoveryAuditActionNames.RetryDenied,
                    Resource: null,
                    Outcome: "denied",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(runId),
                    $"runId={runId};reason=permission_missing",
                    RunId: runId),
                ct).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
    }

    private async ValueTask TryAuditRunCycleDeniedAsync(CancellationToken ct)
    {
        if (_audit is null) return;
        try
        {
            var actor = await _principal.GetCurrentActorAsync(ct).ConfigureAwait(false);
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    actor?.Id ?? new ActorId("system"),
                    RecoveryAuditActionNames.RunCycleDenied,
                    Resource: null,
                    Outcome: "denied",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(null),
                    "reason=permission_missing"),
                ct).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
    }

    private async ValueTask TryAuditRunCycleRequestedAsync(string planeId, CancellationToken ct)
    {
        if (_audit is null) return;
        try
        {
            var actor = await _principal.GetCurrentActorAsync(ct).ConfigureAwait(false);
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    actor?.Id ?? new ActorId("system"),
                    RecoveryAuditActionNames.SchedulesRunCycleRequested,
                    Resource: null,
                    Outcome: "completed",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(planeId),
                    $"runtimePlaneId={planeId}"),
                ct).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
    }
}
