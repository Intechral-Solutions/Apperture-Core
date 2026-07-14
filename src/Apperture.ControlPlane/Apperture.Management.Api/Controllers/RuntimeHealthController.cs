using Apperture.ControlPlane.Security;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/control")]
[Authorize]
public sealed class RuntimeHealthController : ControllerBase
{
    private readonly IRuntimeHealthQueryService _health;
    private readonly ISchedulerStatusQueryService _schedulerStatus;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;

    public RuntimeHealthController(
        IRuntimeHealthQueryService health,
        ISchedulerStatusQueryService schedulerStatus,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation)
    {
        _health = health;
        _schedulerStatus = schedulerStatus;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
    }

    /// <summary>
    /// Gets a unified runtime health snapshot including module, schedule, blocked work, and anomaly diagnostics. Epic 30. Epic 34: requires runtime.health.read.
    /// </summary>
    [HttpGet("runtime-health")]
    [ProducesResponseType(typeof(RuntimeHealthSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RuntimeHealthSnapshot>> GetRuntimeHealth(
        [FromQuery] string? runtimePlaneId = "local",
        [FromQuery] int blockedDeferredWindowHours = 24,
        [FromQuery] int scheduleFailureWindowHours = 24,
        CancellationToken cancellationToken = default)
    {
        if (!await CanReadRuntimeHealthAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();
        var snapshot = await _health.GetSnapshotAsync(
            runtimePlaneId,
            Math.Max(1, Math.Min(blockedDeferredWindowHours, 168)),
            Math.Max(1, Math.Min(scheduleFailureWindowHours, 168)),
            cancellationToken).ConfigureAwait(false);
        return Ok(snapshot);
    }

    /// <summary>
    /// SWM-6/SWM-7: background scheduler timer-loop status from persisted heartbeat (WorkerHost),
    /// including WorkerRuntime projection. Requires runtime.health.read.
    /// </summary>
    [HttpGet("scheduler-status")]
    [ProducesResponseType(typeof(SchedulerStatusSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SchedulerStatusSnapshot>> GetSchedulerStatus(
        [FromQuery] string? runtimePlaneId = "local",
        CancellationToken cancellationToken = default)
    {
        if (!await CanReadRuntimeHealthAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();
        var status = await _schedulerStatus.GetStatusAsync(runtimePlaneId, cancellationToken).ConfigureAwait(false);
        return Ok(status);
    }

    private async Task<bool> CanReadRuntimeHealthAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return false;
        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.RuntimeHealthRead, new ResourceDescriptor(ResourceTypeConstants.RuntimeHealth, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
    }
}
