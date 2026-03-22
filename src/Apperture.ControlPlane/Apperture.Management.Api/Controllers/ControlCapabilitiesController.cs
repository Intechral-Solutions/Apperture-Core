using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Operator UI capabilities for permission-aware rendering. Epic 34.
/// </summary>
[ApiController]
[Route("api/control")]
[Authorize]
public sealed class ControlCapabilitiesController : ControllerBase
{
    private readonly IOperatorCapabilitiesQueryService _capabilities;

    public ControlCapabilitiesController(IOperatorCapabilitiesQueryService capabilities)
    {
        _capabilities = capabilities;
    }

    /// <summary>
    /// Gets operator capabilities for the current principal.
    /// </summary>
    [HttpGet("capabilities")]
    [ProducesResponseType(typeof(OperatorCapabilitiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OperatorCapabilitiesDto>> GetCapabilities(CancellationToken cancellationToken = default)
    {
        var dto = await _capabilities.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        return Ok(dto);
    }
}
