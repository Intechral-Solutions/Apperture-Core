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

    public IntegrationsController(IIntegrationManagementService integrationService)
    {
        _integrationService = integrationService;
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
            var request = new CreateIntegrationRequest(body.DisplayName, body.ModuleId, body.ScopeKey);
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
            var request = new UpdateIntegrationRequest(body.DisplayName, integrationState);
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
        i.CreatedAt,
        i.UpdatedAt);
}

public sealed record IntegrationDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateIntegrationRequestBody(
    string DisplayName,
    string ModuleId,
    string? ScopeKey);

public sealed record UpdateIntegrationRequestBody(
    string? DisplayName,
    string? State);
