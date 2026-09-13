using Apperture.Core.Application.PlatformEvents;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>Read-only event catalog for notification definition authoring (Epic 77 Phase 8A).</summary>
[ApiController]
[Route("api/platform-events/catalog")]
[Authorize]
public sealed class PlatformEventsCatalogController : ControllerBase
{
    private readonly IPlatformEventAuthoringCatalogService _catalog;

    public PlatformEventsCatalogController(IPlatformEventAuthoringCatalogService catalog) =>
        _catalog = catalog;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformEventCatalogEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlatformEventCatalogEntryDto>>> List(
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _catalog.ListAsync(cancellationToken).ConfigureAwait(false);
            return Ok(rows.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{*eventType}")]
    [ProducesResponseType(typeof(PlatformEventCatalogEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlatformEventCatalogEntryDto>> Get(
        string eventType,
        CancellationToken cancellationToken)
    {
        try
        {
            var decoded = Uri.UnescapeDataString(eventType);
            var row = await _catalog.GetAsync(decoded, cancellationToken).ConfigureAwait(false);
            return row is null ? NotFound() : Ok(ToDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static PlatformEventCatalogEntryDto ToDto(PlatformEventAuthoringCatalogEntry e) =>
        new(
            e.Type,
            e.Source,
            e.DisplayName,
            e.Description,
            e.AllowedSeverities,
            e.RequiresIntegrationId,
            e.RequiresScheduleId,
            e.RequiresRunId,
            e.RequiredDataProperties,
            e.TemplateVariables);
}
