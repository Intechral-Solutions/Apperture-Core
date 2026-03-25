using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Module procedures scoped to an integration (ADR-0029).
/// </summary>
[ApiController]
[Route("api/integrations/{integrationId}/procedures")]
[Authorize]
public sealed class IntegrationProceduresController : ControllerBase
{
    private readonly IIntegrationProcedureService _procedures;

    public IntegrationProceduresController(IIntegrationProcedureService procedures)
    {
        _procedures = procedures;
    }

    [HttpGet]
    public async Task<ActionResult<IntegrationProcedureListResponse>> List(string integrationId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _procedures.ListAvailableAsync(integrationId, cancellationToken);
            if (result is null)
                return NotFound();
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{procedureId}")]
    public async Task<ActionResult<IntegrationProcedureDetailDto>> Get(string integrationId, string procedureId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _procedures.GetProcedureAsync(integrationId, procedureId, cancellationToken);
            if (result is null)
                return NotFound();
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("{procedureId}/launch")]
    public async Task<ActionResult> Launch(
        string integrationId,
        string procedureId,
        [FromBody] IntegrationProcedureLaunchRequest? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _procedures.LaunchAsync(
                integrationId,
                procedureId,
                body ?? new IntegrationProcedureLaunchRequest(null, null, null),
                cancellationToken);
            if (result is null)
                return NotFound(new { error = "Integration, procedure, or executable target was not found." });

            return Accepted(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not ready", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
