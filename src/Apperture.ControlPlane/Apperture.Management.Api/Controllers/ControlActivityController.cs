using Apperture.Core.Application.ControlFacade;
using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/control")]
[Authorize]
public sealed class ControlActivityController : ControllerBase
{
    private readonly IControlActivityQueryService _activity;

    public ControlActivityController(IControlActivityQueryService activity)
    {
        _activity = activity;
    }

    /// <summary>
    /// Unified activity feed for operator monitoring (executions + durable runtime control requests).
    /// </summary>
    [HttpGet("activity")]
    [ProducesResponseType(typeof(IReadOnlyList<ControlActivitySummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ControlActivitySummaryDto>>> GetActivity(
        [FromQuery] DateTimeOffset? createdFromUtc,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        var createdFrom = createdFromUtc ?? DateTimeOffset.UtcNow.AddHours(-24);
        var items = await _activity.ListRecentActivityAsync(createdFrom, Math.Max(1, Math.Min(take, 100)), cancellationToken: cancellationToken);
        return Ok(items);
    }
}

