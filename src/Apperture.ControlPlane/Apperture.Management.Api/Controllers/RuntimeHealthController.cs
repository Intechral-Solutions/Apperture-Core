using Apperture.Core.Application.RuntimeHealth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/control")]
[Authorize]
public sealed class RuntimeHealthController : ControllerBase
{
    private readonly IRuntimeHealthQueryService _health;

    public RuntimeHealthController(IRuntimeHealthQueryService health)
    {
        _health = health;
    }

    /// <summary>
    /// Gets a unified runtime health snapshot including module, schedule, blocked work, and anomaly diagnostics. Epic 30.
    /// </summary>
    [HttpGet("runtime-health")]
    [ProducesResponseType(typeof(RuntimeHealthSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<RuntimeHealthSnapshot>> GetRuntimeHealth(
        [FromQuery] string? runtimePlaneId = "local",
        [FromQuery] int blockedDeferredWindowHours = 24,
        [FromQuery] int scheduleFailureWindowHours = 24,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _health.GetSnapshotAsync(
            runtimePlaneId,
            Math.Max(1, Math.Min(blockedDeferredWindowHours, 168)),
            Math.Max(1, Math.Min(scheduleFailureWindowHours, 168)),
            cancellationToken).ConfigureAwait(false);
        return Ok(snapshot);
    }
}
