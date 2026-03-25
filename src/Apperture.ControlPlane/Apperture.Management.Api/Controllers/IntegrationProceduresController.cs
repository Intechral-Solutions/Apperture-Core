using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Module procedures scoped to an integration (ADR-0029, Epic 56A hardened semantics).
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
    }

    [HttpPost("{procedureId}/launch")]
    public async Task<IActionResult> Launch(
        string integrationId,
        string procedureId,
        [FromBody] IntegrationProcedureLaunchBody? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new IntegrationProcedureLaunchRequest(
                IntegrationProcedureLaunchBody.CoerceParameters(body?.Parameters),
                body?.IdempotencyKey,
                body?.CorrelationId);
            var outcome = await _procedures.LaunchAsync(
                integrationId,
                procedureId,
                request,
                cancellationToken);

            return outcome switch
            {
                IntegrationProcedureLaunchOutcome.Succeeded ok => Accepted(ok.Response),
                IntegrationProcedureLaunchOutcome.Failed f => LaunchError(f.Code, f.Message),
                _ => StatusCode(500, new { code = "unknown", error = "Unexpected launch outcome." })
            };
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static ObjectResult LaunchError(IntegrationProcedureLaunchErrorCode code, string message)
    {
        var status = code.ToHttpStatus();
        return new ObjectResult(new { code = code.ToApiCode(), error = message })
        {
            StatusCode = status
        };
    }
}
