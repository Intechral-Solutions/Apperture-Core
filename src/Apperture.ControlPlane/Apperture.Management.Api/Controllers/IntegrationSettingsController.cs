using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Management.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Management.Api.Controllers;

/// <summary>Integration-scoped settings (Epic 118). Value scope and provider context derive from route integrationId.</summary>
[ApiController]
[Route("api/integrations/{integrationId}/settings")]
[Authorize]
public sealed class IntegrationSettingsController : ControllerBase
{
    private readonly IIntegrationSettingsService _service;
    private readonly IIntegrationSettingDefinitionRegistry _definitionRegistry;
    private readonly IIntegrationRepository _integrations;
    private readonly IntegrationSettingsAuthorization _authorization;
    private readonly PlatformSettingOptionProviderCoordinator _optionProviderCoordinator;

    public IntegrationSettingsController(
        IIntegrationSettingsService service,
        IIntegrationSettingDefinitionRegistry definitionRegistry,
        IIntegrationRepository integrations,
        IntegrationSettingsAuthorization authorization,
        PlatformSettingOptionProviderCoordinator optionProviderCoordinator)
    {
        _service = service;
        _definitionRegistry = definitionRegistry;
        _integrations = integrations;
        _authorization = authorization;
        _optionProviderCoordinator = optionProviderCoordinator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformSettingResolvedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PlatformSettingResolvedDto>>> List(
        string integrationId,
        CancellationToken cancellationToken)
    {
        if (!await IntegrationExistsAsync(integrationId, cancellationToken))
            return NotFound();

        try
        {
            await _authorization.RequireIntegrationReadAsync(integrationId, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        var rows = await _service.ListAsync(integrationId, cancellationToken).ConfigureAwait(false);
        return Ok(rows.Select(PlatformSettingsDtoMapper.ToDto).ToList());
    }

    [HttpGet("{key}")]
    [ProducesResponseType(typeof(PlatformSettingResolvedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlatformSettingResolvedDto>> Get(
        string integrationId,
        string key,
        CancellationToken cancellationToken)
    {
        if (!await IntegrationExistsAsync(integrationId, cancellationToken))
            return NotFound();

        try
        {
            await _authorization.RequireIntegrationReadAsync(integrationId, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        var row = await _service.GetAsync(integrationId, key, cancellationToken).ConfigureAwait(false);
        return row is null ? NotFound() : Ok(PlatformSettingsDtoMapper.ToDto(row));
    }

    [HttpPut("{key}")]
    [ProducesResponseType(typeof(PlatformSettingResolvedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlatformSettingResolvedDto>> Put(
        string integrationId,
        string key,
        [FromBody] UpdatePlatformSettingRequestBody body,
        CancellationToken cancellationToken)
    {
        if (!await IntegrationExistsAsync(integrationId, cancellationToken))
            return NotFound();

        try
        {
            await _authorization.RequireIntegrationManageAsync(integrationId, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        try
        {
            var updated = await _service.SetAsync(
                integrationId,
                key,
                PlatformSettingsDtoMapper.ToIntegrationPayload(body),
                body.RowVersionBase64,
                cancellationToken,
                ReauthorizeManageAsync(integrationId)).ConfigureAwait(false);
            return Ok(PlatformSettingsDtoMapper.ToDto(updated));
        }
        catch (UnauthorizedAccessException)
        {
            // The same check as above, re-run under the integration lock after an ownership move.
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            // Deleted between the existence check above and the change's own locked read.
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex is not AuditInvariantException)
        {
            // A programming error of the audited write is not a refusal: it surfaces as the internal error it is.
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Setting was modified elsewhere. Refresh and try again." });
        }
    }

    [HttpDelete("{key}")]
    [ProducesResponseType(typeof(PlatformSettingResolvedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlatformSettingResolvedDto>> Delete(
        string integrationId,
        string key,
        CancellationToken cancellationToken)
    {
        if (!await IntegrationExistsAsync(integrationId, cancellationToken))
            return NotFound();

        try
        {
            await _authorization.RequireIntegrationManageAsync(integrationId, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        try
        {
            var updated = await _service.ResetAsync(integrationId, key, cancellationToken, ReauthorizeManageAsync(integrationId))
                .ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(PlatformSettingsDtoMapper.ToDto(updated));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Setting was modified elsewhere. Refresh and try again." });
        }
    }

    [HttpGet("{key}/options")]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformSettingAllowedValueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PlatformSettingAllowedValueDto>>> Options(
        string integrationId,
        string key,
        CancellationToken cancellationToken)
    {
        if (!await IntegrationExistsAsync(integrationId, cancellationToken))
            return NotFound();

        try
        {
            await _authorization.RequireIntegrationReadAsync(integrationId, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        var def = _definitionRegistry.TryGet(key);
        if (def is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(def.OptionProviderKey))
            return BadRequest(new { message = $"Setting '{key}' is not provider-backed." });

        try
        {
            var options = await _optionProviderCoordinator
                .GetOptionsForIntegrationSettingsReadAsync(def, integrationId, cancellationToken)
                .ConfigureAwait(false);

            return Ok(options.Select(o => new PlatformSettingAllowedValueDto
            {
                Value = o.Value,
                Label = o.Label,
                Description = o.Description
            }).ToList());
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// The manage check this action already made, for the service to run again once it holds the integration row lock: an
    /// ownership move that committed in between is then seen by the check, and none can commit after it.
    /// </summary>
    private Func<CancellationToken, ValueTask> ReauthorizeManageAsync(string integrationId) =>
        async ct => await _authorization.RequireIntegrationManageAsync(integrationId, ct).ConfigureAwait(false);

    private async Task<bool> IntegrationExistsAsync(string integrationId, CancellationToken cancellationToken)
    {
        var integration = await _integrations.GetByIdAsync(new IntegrationId(integrationId), cancellationToken)
            .ConfigureAwait(false);
        return integration is not null;
    }
}
