using Apperture.Core.Application.ControlFacade;
using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/control")]
[Authorize]
public sealed class ControlCenterController : ControllerBase
{
    private readonly IControlCenterQueryService _center;

    public ControlCenterController(IControlCenterQueryService center)
    {
        _center = center;
    }

    [HttpGet("center")]
    [ProducesResponseType(typeof(ControlCenterDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ControlCenterDto>> GetCenter(
        [FromQuery] DateTimeOffset? createdFromUtc,
        [FromQuery] int takeRecentActivity = 25,
        [FromQuery] int takeAttention = 10,
        [FromQuery] string? resourceContextId = null,
        CancellationToken cancellationToken = default)
    {
        var from = createdFromUtc ?? DateTimeOffset.UtcNow.AddHours(-24);
        var dto = await _center.GetControlCenterAsync(from, takeRecentActivity, takeAttention, resourceContextId, cancellationToken).ConfigureAwait(false);
        return Ok(dto);
    }
}

