using Apperture.Core.Application.Recovery;
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
    private readonly IRecoveryGovernanceService _recoveryGovernance;
    private readonly IAuditService? _audit;
    private readonly IPrincipalContext _principal;

    public RecoveryController(
        IRecoveryQueryService recovery,
        IWorkflowRetryService retry,
        IScheduleDispatchRunner scheduleRunner,
        IRecoveryGovernanceService recoveryGovernance,
        IPrincipalContext principal,
        IAuditService? audit = null)
    {
        _recovery = recovery;
        _retry = retry;
        _scheduleRunner = scheduleRunner;
        _recoveryGovernance = recoveryGovernance;
        _principal = principal;
        _audit = audit;
    }

    /// <summary>
    /// Lists blocked deferred work with remediation eligibility.
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
    /// Operation blocked work is inspectable only; retry is not supported. Returns 400 with inspectionOnly.
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
                error = "Operation retry not yet supported.",
                runId,
                definitionKind = "operation",
                inspectionOnly = true,
                message = "Operation blocked work is inspectable via GET blocked-work; platform recovery does not yet support operation retry."
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
    /// Blocked schedules will re-evaluate at their NextDueUtc when the cycle runs.
    /// </summary>
    [HttpPost("schedules/run-cycle")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> RunScheduleCycle(
        [FromQuery] string? runtimePlaneId = "local",
        CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            await TryAuditRunCycleDeniedAsync(cancellationToken).ConfigureAwait(false);
            return Forbid();
        }
        var planeId = new RuntimePlaneId(runtimePlaneId ?? "local");
        await _scheduleRunner.RunCycleAsync(planeId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        await TryAuditRunCycleRequestedAsync(planeId.Value, cancellationToken).ConfigureAwait(false);
        return Accepted(new
        {
            runtimePlaneId = planeId.Value,
            action = "global_scheduler_run_cycle",
            message = "Global scheduler cycle completed. Processes schedules due at cycle time; not schedule-specific."
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
