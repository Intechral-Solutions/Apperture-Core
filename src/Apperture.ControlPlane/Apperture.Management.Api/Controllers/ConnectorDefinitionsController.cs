using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/connector-definitions")]
[Authorize]
public sealed class ConnectorDefinitionsController : ControllerBase
{
    private readonly IConnectorDefinitionCatalogReadService _readService;

    public ConnectorDefinitionsController(IConnectorDefinitionCatalogReadService readService) =>
        _readService = readService;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConnectorDefinitionCatalogDto>>> List(CancellationToken cancellationToken)
    {
        try
        {
            var list = await _readService.ListAsync(cancellationToken).ConfigureAwait(false);
            return Ok(list.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ConnectorDefinitionCatalogDto>> Get(string id, CancellationToken cancellationToken)
    {
        try
        {
            var definition = await _readService.GetByIdAsync(new ConnectorDefinitionId(id), cancellationToken)
                .ConfigureAwait(false);
            if (definition is null)
                return NotFound();
            return Ok(ToDto(definition));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static ConnectorDefinitionCatalogDto ToDto(ConnectorDefinition d) => new(
        d.Id.Value,
        d.Name,
        d.ModuleId,
        d.Description,
        d.ConnectionFields.Select(f => new ConnectionFieldCatalogDto(f.Key, f.DisplayName, f.IsSecret, f.Description))
            .ToList(),
        d.Capabilities.ToString());
}
