using Apperture.Core.Application.ComponentCatalog;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Read-only Component catalog. A Component is a projection over the specialized family services, so
/// this controller exposes no mutation: management always happens on the authoritative family surface.
/// </summary>
[ApiController]
[Route("api/components")]
[Authorize]
public sealed class ComponentsController : ControllerBase
{
    private readonly IComponentCatalog _catalog;

    public ComponentsController(IComponentCatalog catalog) => _catalog = catalog;

    /// <summary>
    /// 200 with whatever the caller may read; 403 only when every family consulted was denied.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ComponentCatalogResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ComponentCatalogResultDto>> List(
        [FromQuery] string? kind = null,
        [FromQuery] string? origin = null,
        [FromQuery] string? moduleId = null,
        [FromQuery] string? q = null,
        [FromQuery] int take = ComponentCatalogQuery.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(kind) && !ComponentKinds.IsKnown(kind))
            return BadRequest(new { error = $"Unknown component kind '{kind}'." });
        if (!string.IsNullOrWhiteSpace(origin) && !ComponentOrigins.IsKnown(origin))
            return BadRequest(new { error = $"Unknown component origin '{origin}'." });

        try
        {
            var result = await _catalog
                .QueryAsync(new ComponentCatalogQuery(kind, origin, moduleId, q, take), cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToDto(result));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 404 for an unknown kind, an unknown native id, or a family the caller may not read. Deliberately
    /// never 403: a denial would confirm that the Component exists.
    /// </summary>
    [HttpGet("{kind}/{nativeId}")]
    [ProducesResponseType(typeof(ComponentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComponentDetailDto>> Get(
        string kind,
        string nativeId,
        CancellationToken cancellationToken = default)
    {
        if (!ComponentKinds.IsKnown(kind))
            return NotFound();

        var detail = await _catalog.GetAsync(new ComponentKey(kind, nativeId), cancellationToken).ConfigureAwait(false);
        return detail is null ? NotFound() : Ok(ToDto(detail));
    }

    private static ComponentCatalogResultDto ToDto(ComponentCatalogResult result) => new(
        result.Items.Select(ToDto).ToList(),
        result.AvailableKinds,
        result.AvailableModules.Select(m => new ComponentModuleRefDto(m.ModuleId)).ToList(),
        result.TotalMatching);

    private static ComponentSummaryDto ToDto(ComponentSummary s) => new(
        s.Key.Kind, s.Key.NativeId, s.DisplayName, s.Description, s.Origin, s.SupplyingModuleId);

    private static ComponentDetailDto ToDto(ComponentDetail d) => new(
        d.Summary.Key.Kind,
        d.Summary.Key.NativeId,
        d.Summary.DisplayName,
        d.Summary.Description,
        d.Summary.Origin,
        d.Summary.SupplyingModuleId,
        d.SupplyingModule is null
            ? null
            : new ModuleAttributionDto(d.SupplyingModule.ModuleId, d.SupplyingModule.DisplayName, d.SupplyingModule.Version, d.SupplyingModule.State),
        d.Connector is null
            ? null
            : new ConnectorComponentDetailDto(
                d.Connector.Fields
                    .Select(f => new ConnectionFieldCatalogDto(f.Key, f.DisplayName, f.IsSecret, f.Description, f.IsRequired))
                    .ToList(),
                d.Connector.SupportsConnectionVerification),
        d.NotificationDefinition is null
            ? null
            : new NotificationDefinitionComponentDetailDto(
                d.NotificationDefinition.PrimaryEventType,
                d.NotificationDefinition.SourceKind,
                d.NotificationDefinition.Scope,
                d.NotificationDefinition.Mutability,
                d.NotificationDefinition.DefaultSeverity,
                d.NotificationDefinition.IsEnabled,
                d.NotificationDefinition.ToastEnabledByDefault,
                d.NotificationDefinition.UpdatedAtUtc));
}
