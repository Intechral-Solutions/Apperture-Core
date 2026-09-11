using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClientInventoryItemDto = Apperture.Management.Client.InventoryItemDto;
using ClientInventoryQueryResult = Apperture.Management.Client.InventoryQueryResult;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Administrative explorer over existing configuration resources and recent activity.
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public sealed class InventoryController : ControllerBase
{
    private readonly IInventoryQueryService _inventory;

    public InventoryController(IInventoryQueryService inventory) => _inventory = inventory;

    [HttpGet]
    [ProducesResponseType(typeof(ClientInventoryQueryResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClientInventoryQueryResult>> Get(
        [FromQuery] string mode = InventoryModes.Resources,
        [FromQuery] string? type = null,
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sort = null,
        [FromQuery] int take = InventoryQueryService.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var result = await _inventory
            .QueryAsync(new InventoryQuery(mode, type, q, status, sort, take), cancellationToken)
            .ConfigureAwait(false);
        return Ok(ToClient(result));
    }

    private static ClientInventoryQueryResult ToClient(InventoryQueryResult result) =>
        new(
            result.Items.Select(i => new ClientInventoryItemDto(
                i.Id,
                i.Type,
                i.TypeLabel,
                i.Name,
                i.Status,
                i.StatusLabel,
                i.Href,
                i.UpdatedAtUtc,
                i.Mode)).ToList(),
            result.AvailableTypes);
}
