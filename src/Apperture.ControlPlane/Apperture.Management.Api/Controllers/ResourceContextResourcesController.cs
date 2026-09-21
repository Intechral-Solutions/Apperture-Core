using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// The resources a ResourceContext (an Application, or Shared) currently OWNS
/// (<c>resource.ResourceContextId == id</c>): Integrations, Connections and Schedules. Resources merely
/// referenced by an owned resource (for example a Shared Connection bound by an Application Integration) are
/// not listed, and neither is history (Runs, Audit) or anything that is not an owned operational resource.
/// </summary>
/// <remarks>
/// This reads the same normalized resource model as the Config Inventory explorer
/// (<see cref="IInventoryQueryService"/>) with a fixed Context and a three-family restriction, but it is a
/// separate endpoint so this operational view does not depend on the diagnostic explorer's contract. There is
/// no permission of its own: each family is gated by its existing read permission inside the query, and a
/// family the caller cannot read is omitted entirely (no rows, and it is not counted in
/// <see cref="ResourceContextResourcesResult.TotalMatching"/>).
/// </remarks>
[ApiController]
[Route("api/resource-contexts/{id}/resources")]
[Authorize]
public sealed class ResourceContextResourcesController(
    IInventoryQueryService inventory,
    IResourceContextService resourceContexts) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ResourceContextResourcesResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResourceContextResourcesResult>> Get(
        string id,
        [FromQuery] string? type = null,
        [FromQuery] string? q = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> families = ResourceContextResourceTypes.All;
        if (!string.IsNullOrWhiteSpace(type))
        {
            var requested = type.Trim();
            if (!ResourceContextResourceTypes.All.Contains(requested, StringComparer.Ordinal))
                return BadRequest(new { error = $"Type '{type}' is not an application resource type." });
            families = [requested];
        }

        var contextId = ResourceContextIds.IsShared(id) ? ResourceContextIds.SharedValue : id;
        if (await resourceContexts.GetAsync(new ResourceContextId(contextId), cancellationToken).ConfigureAwait(false) is null)
            return NotFound();

        var result = await inventory
            .QueryAsync(
                new InventoryQuery(
                    Mode: InventoryModes.Resources,
                    Q: q,
                    Sort: InventorySorts.Name,
                    Take: InventoryQueryService.MaxTake,
                    Context: InventoryContextFilter.ForContext(contextId),
                    Types: families),
                cancellationToken)
            .ConfigureAwait(false);

        return Ok(new ResourceContextResourcesResult(
            result.Items
                .Select(i => new ResourceContextResourceDto(i.Id, i.Type, i.TypeLabel, i.Name, i.Status, i.Href))
                .ToList(),
            result.TotalMatching));
    }
}
