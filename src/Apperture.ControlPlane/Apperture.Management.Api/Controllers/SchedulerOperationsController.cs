using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Environment;
using Apperture.ControlPlane.Security;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    private readonly IAuditService? _audit;

    public SchedulerOperationsController(
        IScheduleDispatchRunner scheduleRunner,
        IRecoveryGovernanceService recoveryGovernance,
        IPrincipalContext principal,
        IAuditService? audit = null)
    {
        _scheduleRunner = scheduleRunner;
        _recoveryGovernance = recoveryGovernance;
        _principal = principal;
        _audit = audit;
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
            await TryAuditAsync(RecoveryAuditActionNames.RunCycleDenied, "scheduler.run_cycle denied", cancellationToken)
                .ConfigureAwait(false);
            return Forbid();
        }

        var planeId = new RuntimePlaneId(runtimePlaneId ?? ScheduleRuntimePlaneDefaults.ExpectedManagementPlaneId);
        var result = await ExecuteRunCycleAsync(planeId, cancellationToken).ConfigureAwait(false);
        await TryAuditAsync(
                RecoveryAuditActionNames.SchedulesRunCycleRequested,
                $"scheduler.run_cycle plane={planeId.Value}",
                cancellationToken)
            .ConfigureAwait(false);
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
            await TryAuditAsync(RecoveryAuditActionNames.RunCycleDenied, "scheduler.recover_stale denied", cancellationToken)
                .ConfigureAwait(false);
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
        await TryAuditAsync(
                RecoveryAuditActionNames.SchedulesRunCycleRequested,
                $"scheduler.recover_stale plane={planeId.Value};recovered={cycle.RecoveredStaleCount}",
                cancellationToken)
            .ConfigureAwait(false);
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

    private async ValueTask TryAuditAsync(string action, string details, CancellationToken ct)
    {
        if (_audit is null)
            return;
        try
        {
            var actor = await _principal.GetCurrentActorAsync(ct).ConfigureAwait(false);
            await _audit.AppendAsync(
                    new AuditRecord(
                        new AuditRecordId(Guid.NewGuid().ToString("N")),
                        actor?.Id ?? new ActorId("system:unknown"),
                        action,
                        Resource: null,
                        Outcome: action.Contains("denied", StringComparison.OrdinalIgnoreCase) ? "denied" : "requested",
                        Timestamp: DateTimeOffset.UtcNow,
                        CorrelationId: new CorrelationId(null),
                        Details: details),
                    ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* best-effort */
        }
    }
}
