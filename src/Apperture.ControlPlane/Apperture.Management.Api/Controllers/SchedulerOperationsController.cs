using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditEvent = Apperture.Core.Application.Audit.AuditEvent;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// SWM-8: narrow operator scheduler operations (manual run-cycle + stale-only recovery).
/// Gated by <c>recovery.manage</c>. Does not restart WorkerHost or write scheduler heartbeats.
/// </summary>
[ApiController]
[Route("api/control/scheduler")]
[Authorize]
public sealed class SchedulerOperationsController : ControllerBase
{
    private readonly IScheduleDispatchRunner _scheduleRunner;
    private readonly IRecoveryGovernanceService _recoveryGovernance;
    private readonly IPrincipalContext _principal;
    private readonly IAuditEventWriter _events;

    public SchedulerOperationsController(
        IScheduleDispatchRunner scheduleRunner,
        IRecoveryGovernanceService recoveryGovernance,
        IPrincipalContext principal,
        IAuditEventWriter events)
    {
        _scheduleRunner = scheduleRunner;
        _recoveryGovernance = recoveryGovernance;
        _principal = principal;
        _events = events;
    }

    /// <summary>
    /// Runs one scheduler evaluation cycle for the plane (alias of recovery run-cycle).
    /// Does not write the SWM-6 WorkerHost heartbeat.
    /// </summary>
    [HttpPost("run-cycle")]
    [ProducesResponseType(typeof(SchedulerManualOperationResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SchedulerManualOperationResult>> RunCycle(
        [FromQuery] string? runtimePlaneId = "local",
        CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            await RecordAsync(AuditEvents.SchedulerOperationDenied("run_cycle"), cancellationToken).ConfigureAwait(false);
            return Forbid();
        }

        var planeId = new RuntimePlaneId(runtimePlaneId ?? ScheduleRuntimePlaneDefaults.ExpectedManagementPlaneId);
        var result = await ExecuteRunCycleAsync(planeId, cancellationToken).ConfigureAwait(false);

        // The outcome is what the cycle reported: succeeded, or failed when it threw.
        await RecordAsync(
            AuditEvents.SchedulerOperationRequested("run_cycle", planeId.Value, recoveredCount: null, failed: result.ErrorSummary is not null),
            cancellationToken).ConfigureAwait(false);
        return Accepted(result);
    }

    /// <summary>
    /// Recovers only stale InProgress schedule claims. Does not evaluate newly due fires.
    /// Does not write the SWM-6 WorkerHost heartbeat.
    /// </summary>
    [HttpPost("recover-stale")]
    [ProducesResponseType(typeof(SchedulerManualOperationResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SchedulerManualOperationResult>> RecoverStale(
        [FromQuery] string? runtimePlaneId = "local",
        CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
        {
            await RecordAsync(AuditEvents.SchedulerOperationDenied("recover_stale"), cancellationToken).ConfigureAwait(false);
            return Forbid();
        }

        var planeId = new RuntimePlaneId(runtimePlaneId ?? ScheduleRuntimePlaneDefaults.ExpectedManagementPlaneId);
        string? error = null;
        ScheduleDispatchCycleResult cycle;
        try
        {
            cycle = await _scheduleRunner.RecoverStaleAsync(planeId, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = SchedulerStatusClassifier.SanitizeErrorSummary(ex);
            cycle = new ScheduleDispatchCycleResult(0, 0, 0, HostId: null);
        }

        var result = SchedulerManualOperationMapper.FromCycle(
            planeId.Value,
            SchedulerManualOperationMapper.ActionRecoverStale,
            cycle,
            error);
        await RecordAsync(
            AuditEvents.SchedulerOperationRequested("recover_stale", planeId.Value, cycle.RecoveredStaleCount, failed: error is not null),
            cancellationToken).ConfigureAwait(false);
        return Accepted(result);
    }

    internal async Task<SchedulerManualOperationResult> ExecuteRunCycleAsync(
        RuntimePlaneId planeId,
        CancellationToken cancellationToken)
    {
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

        return SchedulerManualOperationMapper.FromCycle(
            planeId.Value,
            SchedulerManualOperationMapper.ActionRunCycle,
            cycle,
            error);
    }

    /// <summary>
    /// Records a denial or a manual operation as the authenticated principal, or as not signed in when the request maps to no
    /// application session. There is no <c>system</c> fallback. A failure to write is logged and never changes the response.
    /// </summary>
    private async ValueTask RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        await _events.TryWriteAsync(
            auditEvent,
            await AttemptActor.ResolveAsync(_principal, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
}
