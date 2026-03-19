using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/schedules")]
[Authorize]
public sealed class SchedulesController : ControllerBase
{
    private readonly IScheduleGovernanceService _scheduleGovernance;

    public SchedulesController(IScheduleGovernanceService scheduleGovernance)
    {
        _scheduleGovernance = scheduleGovernance;
    }

    [HttpGet("capabilities")]
    [ProducesResponseType(typeof(ScheduleCapabilitiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ScheduleCapabilitiesDto>> GetCapabilities(CancellationToken cancellationToken = default)
    {
        var canManage = await _scheduleGovernance.CanManageAsync(cancellationToken);
        if (!canManage)
            return Forbid();
        return Ok(new ScheduleCapabilitiesDto(true));
    }
}

public sealed record ScheduleCapabilitiesDto(bool CanManage);

