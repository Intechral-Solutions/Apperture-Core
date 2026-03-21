using Apperture.Core.Application.RuntimeComposition;
using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/control")]
[Authorize]
public sealed class RuntimeCompositionController : ControllerBase
{
    private readonly IRuntimeModuleCompositionQueryService _composition;

    public RuntimeCompositionController(IRuntimeModuleCompositionQueryService composition)
    {
        _composition = composition;
    }

    /// <summary>
    /// Host-effective module execution composition for this environment (Epic 27).
    /// </summary>
    [HttpGet("runtime-composition")]
    [ProducesResponseType(typeof(HostEffectiveModuleSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<HostEffectiveModuleSnapshot>> GetRuntimeComposition(CancellationToken cancellationToken)
    {
        var snapshot = await _composition.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return Ok(snapshot);
    }
}
