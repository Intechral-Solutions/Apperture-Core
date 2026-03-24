using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/modules/{moduleId}/slots/{slotName}")]
[Authorize]
public sealed class BindingsController : ControllerBase
{
    private readonly IBindingService _bindingService;

    public BindingsController(IBindingService bindingService)
    {
        _bindingService = bindingService;
    }

    /// <summary>
    /// Gets the binding for a slot.
    /// </summary>
    [HttpGet("binding")]
    public async Task<ActionResult<BindingDto?>> Get(string moduleId, string slotName, CancellationToken cancellationToken)
    {
        try
        {
            var binding = await _bindingService.GetBindingAsync(moduleId, slotName, cancellationToken);
            if (binding is null)
                return Ok((BindingDto?)null);
            return Ok(ToDto(binding));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Legacy route: binding creation is no longer supported without an Integration (Epic 54).
    /// </summary>
    [HttpPost("binding")]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public IActionResult Bind(string moduleId, string slotName, [FromBody] CreateBindingRequestBody? body, CancellationToken cancellationToken)
    {
        return StatusCode(
            StatusCodes.Status410Gone,
            new
            {
                error = "Module-scoped binding creation is no longer supported.",
                usePath = "POST /api/integrations/{integrationId}/bindings",
                message = "Create bindings under the integration that deploys this module; the integration supplies ModuleId and ownership."
            });
    }

    /// <summary>
    /// Gets connections eligible for binding to the slot.
    /// </summary>
    [HttpGet("eligible-connections")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetEligibleConnections(string moduleId, string slotName, [FromQuery] string contractType, CancellationToken cancellationToken)
    {
        try
        {
            var connections = await _bindingService.GetEligibleConnectionsAsync(moduleId, slotName, contractType, cancellationToken);
            return Ok(connections.Select(c => new { Id = c.Id.Value, c.Name, ConnectorDefinitionId = c.ConnectorDefinitionId.Value }).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Revokes the binding for a slot.
    /// </summary>
    [HttpDelete("binding")]
    public async Task<IActionResult> Unbind(string moduleId, string slotName, CancellationToken cancellationToken)
    {
        try
        {
            var binding = await _bindingService.GetBindingAsync(moduleId, slotName, cancellationToken);
            if (binding is null)
                return NoContent();
            await _bindingService.DeleteBindingAsync(binding.Id, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static BindingDto ToDto(Binding b) => new(
        b.Id.Value,
        b.IntegrationId?.Value,
        b.ModuleId,
        b.SlotName,
        b.ContractType,
        b.ConnectionId?.Value,
        b.CapabilityId,
        b.State.ToString());
}

public sealed record BindingDto(
    string Id,
    string? IntegrationId,
    string ModuleId,
    string SlotName,
    string ContractType,
    string? ConnectionId,
    string? CapabilityId,
    string State);

public sealed record CreateBindingRequestBody(string ContractType, string ConnectionId);
