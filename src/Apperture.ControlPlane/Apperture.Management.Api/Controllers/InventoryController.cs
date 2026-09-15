using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;
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
    private readonly IResourceContextService _resourceContexts;

    public InventoryController(IInventoryQueryService inventory, IResourceContextService resourceContexts)
    {
        _inventory = inventory;
        _resourceContexts = resourceContexts;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ClientInventoryQueryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClientInventoryQueryResult>> Get(
        [FromQuery] string mode = InventoryModes.Resources,
        [FromQuery] string? type = null,
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sort = null,
        [FromQuery] int take = InventoryQueryService.DefaultTake,
        [FromQuery] string? context = null,
        CancellationToken cancellationToken = default)
    {
        var (contextFilter, contextError) = await ResolveContextAsync(context, mode, cancellationToken);
        if (contextError is not null)
            return BadRequest(new { error = contextError });

        var result = await _inventory
            .QueryAsync(new InventoryQuery(mode, type, q, status, sort, take, contextFilter), cancellationToken)
            .ConfigureAwait(false);
        return Ok(ToClient(result));
    }

    /// <summary>
    /// Resolves Inventory's own local <c>context</c> query value. This is unrelated to the shell's
    /// <c>?context=</c> View selector: Inventory does not read <c>ResourceContextViewState.Current</c>
    /// and this filter is validated and applied purely against this endpoint's own request.
    /// </summary>
    private async Task<(InventoryContextFilter? Filter, string? Error)> ResolveContextAsync(
        string? context,
        string mode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context) || string.Equals(context, "all", StringComparison.OrdinalIgnoreCase))
            return (InventoryContextFilter.All, null);
        if (string.Equals(context, InventoryContextClassifications.System, StringComparison.OrdinalIgnoreCase))
            return (InventoryContextFilter.System, null);
        if (string.Equals(context, InventoryContextClassifications.NotRecorded, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(mode, InventoryModes.Activity, StringComparison.OrdinalIgnoreCase)
                ? (InventoryContextFilter.NotRecorded, null)
                : (null, "Context 'notRecorded' applies only to Activity.");
        }

        var id = ResourceContextIds.IsShared(context) ? ResourceContextIds.SharedValue : context;
        var resolved = await _resourceContexts.GetAsync(new ResourceContextId(id), ct);
        return resolved is null
            ? (null, $"Context '{context}' not found.")
            : (InventoryContextFilter.ForContext(id), null);
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
                i.Mode,
                i.ResourceContextId,
                i.ResourceContextClassification)).ToList(),
            result.AvailableTypes);
}
