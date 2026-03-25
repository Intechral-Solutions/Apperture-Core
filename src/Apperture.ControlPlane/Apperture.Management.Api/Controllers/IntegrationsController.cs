using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Integration CRUD API. Epic 49.
/// </summary>
[ApiController]
[Route("api/integrations")]
[Authorize]
public sealed class IntegrationsController : ControllerBase
{
    private readonly IIntegrationManagementService _integrationService;
    private readonly IIntegrationReadinessService _readinessService;

    public IntegrationsController(
        IIntegrationManagementService integrationService,
        IIntegrationReadinessService readinessService)
    {
        _integrationService = integrationService;
        _readinessService = readinessService;
    }

    /// <summary>
    /// Lists integrations visible to the current actor.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IntegrationDto>>> List(CancellationToken cancellationToken)
    {
        try
        {
            var integrations = await _integrationService.ListIntegrationsAsync(cancellationToken);
            return Ok(integrations.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Evidence-based readiness: runnable flag and actionable issues from slots, bindings, connections, schedules.
    /// </summary>
    [HttpGet("{id}/readiness")]
    public async Task<ActionResult<IntegrationReadinessProjection>> GetReadiness(string id, CancellationToken cancellationToken)
    {
        try
        {
            var projection = await _readinessService.GetReadinessAsync(new IntegrationId(id), cancellationToken);
            if (projection is null)
                return NotFound();
            return Ok(projection);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Operator view: integration metadata, readiness, required slots, bindings, and schedules in one response.
    /// </summary>
    [HttpGet("{id}/detail")]
    public async Task<ActionResult<IntegrationOperatorDetailProjection>> GetDetail(string id, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _readinessService.GetOperatorDetailAsync(new IntegrationId(id), cancellationToken);
            if (detail is null)
                return NotFound();
            return Ok(detail);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Gets an integration by id.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<IntegrationDto>> Get(string id, CancellationToken cancellationToken)
    {
        try
        {
            var integration = await _integrationService.GetIntegrationAsync(new IntegrationId(id), cancellationToken);
            if (integration is null)
                return NotFound();
            return Ok(ToDto(integration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Creates a new integration.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<IntegrationDto>> Create([FromBody] CreateIntegrationRequestBody body, CancellationToken cancellationToken)
    {
        try
        {
            var request = new CreateIntegrationRequest(body.DisplayName, body.ModuleId, body.ScopeKey, body.Settings);
            var integration = await _integrationService.CreateIntegrationAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = integration.Id.Value }, ToDto(integration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Updates integration metadata.
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<ActionResult<IntegrationDto>> Update(string id, [FromBody] UpdateIntegrationRequestBody body, CancellationToken cancellationToken)
    {
        try
        {
            IntegrationState? integrationState = null;
            if (body.State is not null && Enum.TryParse<IntegrationState>(body.State, ignoreCase: true, out var parsed))
                integrationState = parsed;
            var request = new UpdateIntegrationRequest(body.DisplayName, integrationState, body.Settings);
            var integration = await _integrationService.UpdateIntegrationAsync(new IntegrationId(id), request, cancellationToken);
            return Ok(ToDto(integration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Activates an integration for operational execution.
    /// </summary>
    [HttpPost("{id}/activate")]
    public async Task<ActionResult<IntegrationDto>> Activate(string id, CancellationToken cancellationToken)
    {
        try
        {
            var integration = await _integrationService.ActivateIntegrationAsync(new IntegrationId(id), cancellationToken);
            return Ok(ToDto(integration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Deactivates an integration while preserving configuration.
    /// </summary>
    [HttpPost("{id}/deactivate")]
    public async Task<ActionResult<IntegrationDto>> Deactivate(string id, CancellationToken cancellationToken)
    {
        try
        {
            var integration = await _integrationService.DeactivateIntegrationAsync(new IntegrationId(id), cancellationToken);
            return Ok(ToDto(integration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Deletes an integration.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _integrationService.DeleteIntegrationAsync(new IntegrationId(id), cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static IntegrationDto ToDto(DomainIntegration i) => new(
        i.Id.Value,
        i.DisplayName,
        i.ModuleId,
        i.ScopeKey,
        i.State.ToString(),
        i.Settings,
        i.CreatedAt,
        i.UpdatedAt);
}

public sealed record IntegrationDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string State,
    IReadOnlyDictionary<string, string>? Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateIntegrationRequestBody(
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    IReadOnlyDictionary<string, string>? Settings = null);

public sealed record UpdateIntegrationRequestBody(
    string? DisplayName,
    string? State,
    IReadOnlyDictionary<string, string>? Settings = null);
