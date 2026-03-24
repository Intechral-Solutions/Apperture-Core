using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Client definitions API. Epic 51.
/// </summary>
[ApiController]
[Route("api/clients")]
[Authorize]
public sealed class ClientsController : ControllerBase
{
    private readonly IClientDefinitionReadService _readService;

    public ClientsController(IClientDefinitionReadService readService)
    {
        _readService = readService;
    }

    /// <summary>
    /// Lists available client definitions.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientDefinitionDto>>> List(CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await _readService.ListAsync(cancellationToken);
            return Ok(definitions.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Gets a client definition by id.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ClientDefinitionDto>> Get(string id, CancellationToken cancellationToken)
    {
        try
        {
            var definition = await _readService.GetByIdAsync(new ClientDefinitionId(id), cancellationToken);
            if (definition is null)
                return NotFound();
            return Ok(ToDto(definition));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static ClientDefinitionDto ToDto(ClientDefinition d) => new(
        d.Id.Value,
        d.DisplayName,
        d.Description,
        d.RequiredConnectorDefinitionId.Value,
        d.ModuleId,
        d.UsesGateway,
        d.Operations.Select(o => new ClientOperationMetadataDto(
            o.OperationKey,
            o.DisplayName,
            o.Description,
            o.IsOperatorFacing,
            o.MayRequireUserInputs,
            o.ParameterHint)).ToList());
}

public sealed record ClientDefinitionDto(
    string Id,
    string DisplayName,
    string? Description,
    string RequiredConnectorDefinitionId,
    string? ModuleId,
    bool UsesGateway,
    IReadOnlyList<ClientOperationMetadataDto> Operations);

public sealed record ClientOperationMetadataDto(
    string OperationKey,
    string DisplayName,
    string? Description,
    bool IsOperatorFacing,
    bool MayRequireUserInputs,
    string? ParameterHint);
