using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.ModuleSdk.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Integration-owned bindings. Epic 50.
/// </summary>
[ApiController]
[Route("api/integrations/{integrationId}/bindings")]
[Authorize]
public sealed class IntegrationBindingsController : ControllerBase
{
    private readonly IIntegrationManagementService _integrationManagement;
    private readonly IBindingRepository _bindingRepository;
    private readonly IBindingService _bindingService;
    private readonly IModuleSlotRegistry _moduleSlotRegistry;

    public IntegrationBindingsController(
        IIntegrationManagementService integrationManagement,
        IBindingRepository bindingRepository,
        IBindingService bindingService,
        IModuleSlotRegistry moduleSlotRegistry)
    {
        _integrationManagement = integrationManagement;
        _bindingRepository = bindingRepository;
        _bindingService = bindingService;
        _moduleSlotRegistry = moduleSlotRegistry;
    }

    /// <summary>
    /// Gets capability slots declared for this Integration's module.
    /// </summary>
    [HttpGet("slots")]
    public async Task<ActionResult<IReadOnlyList<ModuleSlotDto>>> GetModuleSlots(string integrationId, CancellationToken cancellationToken)
    {
        try
        {
            var id = new IntegrationId(integrationId);
            var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
            if (integration is null)
                return NotFound();

            var slots = _moduleSlotRegistry.GetSlotsForModule(integration.ModuleId);
            return Ok(slots.Select(s => new ModuleSlotDto(s.ModuleId, s.SlotName, s.ContractType)).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Lists bindings owned by this Integration.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BindingDto>>> List(string integrationId, CancellationToken cancellationToken)
    {
        try
        {
            var id = new IntegrationId(integrationId);
            var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
            if (integration is null)
                return NotFound();

            var bindings = await _bindingRepository.GetByIntegrationIdAsync(id, cancellationToken);
            return Ok(bindings.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Gets connections eligible for binding to a slot of this Integration's module.
    /// </summary>
    [HttpGet("eligible-connections")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetEligibleConnections(
        string integrationId,
        [FromQuery] string slotName,
        [FromQuery] string contractType,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = new IntegrationId(integrationId);
            var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
            if (integration is null)
                return NotFound();

            var connections = await _bindingService.GetEligibleConnectionsAsync(
                integration.ModuleId,
                slotName,
                contractType,
                integration.ResourceContextId,
                cancellationToken);
            return Ok(connections.Select(c => new { Id = c.Id.Value, c.Name, ConnectorDefinitionId = c.ConnectorDefinitionId.Value }).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Creates a binding for this Integration. ModuleId is taken from the Integration.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BindingDto>> Create(string integrationId, [FromBody] CreateIntegrationBindingRequestBody body, CancellationToken cancellationToken)
    {
        try
        {
            var id = new IntegrationId(integrationId);
            var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
            if (integration is null)
                return NotFound();

            var binding = await _bindingService.CreateBindingAsync(
                integration.ModuleId,
                body.SlotName,
                body.ContractType,
                new ConnectionId(body.ConnectionId),
                id,
                cancellationToken);
            return CreatedAtAction(nameof(List), new { integrationId }, ToDto(binding));
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
    /// Deletes a binding owned by this Integration.
    /// </summary>
    [HttpDelete("{bindingId}")]
    public async Task<IActionResult> Delete(string integrationId, string bindingId, CancellationToken cancellationToken)
    {
        try
        {
            var id = new IntegrationId(integrationId);
            var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
            if (integration is null)
                return NotFound();

            var binding = await _bindingRepository.GetByIdAsync(new BindingId(bindingId), cancellationToken);
            if (binding is null)
                return NotFound();
            if (binding.IntegrationId?.Value != integrationId)
                return NotFound();

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

public sealed record ModuleSlotDto(string ModuleId, string SlotName, string ContractType);
public sealed record CreateIntegrationBindingRequestBody(string SlotName, string ContractType, string ConnectionId);
