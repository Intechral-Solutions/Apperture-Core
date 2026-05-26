using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/config/platform-settings")]
[Authorize]
public sealed class ConfigPlatformSettingsController : ControllerBase
{
    private readonly IPlatformSettingsService _service;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IPlatformSettingDefinitionRegistry _definitionRegistry;
    private readonly IPlatformSettingOptionProviderRegistry _optionProviderRegistry;

    public ConfigPlatformSettingsController(
        IPlatformSettingsService service,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IPlatformSettingDefinitionRegistry definitionRegistry,
        IPlatformSettingOptionProviderRegistry optionProviderRegistry)
    {
        _service = service;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _definitionRegistry = definitionRegistry;
        _optionProviderRegistry = optionProviderRegistry;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformSettingResolvedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PlatformSettingResolvedDto>>> List(CancellationToken cancellationToken)
    {
        if (!await AuthorizeReadAsync(cancellationToken))
            return Forbid();

        var rows = await _service.ListAsync(cancellationToken);
        return Ok(rows.Select(PlatformSettingsDtoMapper.ToDto).ToList());
    }

    [HttpGet("{key}")]
    [ProducesResponseType(typeof(PlatformSettingResolvedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlatformSettingResolvedDto>> Get(string key, CancellationToken cancellationToken)
    {
        if (!await AuthorizeReadAsync(cancellationToken))
            return Forbid();

        var row = await _service.GetAsync(key, cancellationToken);
        return row is null ? NotFound() : Ok(PlatformSettingsDtoMapper.ToDto(row));
    }

    [HttpPut("{key}")]
    [ProducesResponseType(typeof(PlatformSettingResolvedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlatformSettingResolvedDto>> Put(
        string key,
        [FromBody] UpdatePlatformSettingRequestBody body,
        CancellationToken cancellationToken)
    {
        if (!await AuthorizeManageAsync(cancellationToken))
            return Forbid();

        try
        {
            var updated = await _service.SetAsync(
                key,
                PlatformSettingsDtoMapper.ToPayload(body),
                body.RowVersionBase64,
                cancellationToken);
            return Ok(PlatformSettingsDtoMapper.ToDto(updated));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
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
    public async Task<ActionResult<PlatformSettingResolvedDto>> Delete(string key, CancellationToken cancellationToken)
    {
        if (!await AuthorizeManageAsync(cancellationToken))
            return Forbid();

        var updated = await _service.ResetAsync(key, cancellationToken);
        return updated is null ? NotFound() : Ok(PlatformSettingsDtoMapper.ToDto(updated));
    }

    [HttpGet("{key}/options")]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformSettingAllowedValueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PlatformSettingAllowedValueDto>>> Options(
        string key,
        CancellationToken cancellationToken)
    {
        if (!await AuthorizeReadAsync(cancellationToken))
            return Forbid();

        var def = _definitionRegistry.TryGet(key);
        if (def is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(def.OptionProviderKey))
            return BadRequest(new { message = $"Setting '{key}' is not provider-backed." });

        if (!_optionProviderRegistry.TryGet(def.OptionProviderKey, out var provider))
            return BadRequest(new { message = $"No option provider registered for '{def.OptionProviderKey}'." });

        try
        {
            var options = await provider.GetOptionsAsync(def, cancellationToken).ConfigureAwait(false);
            return Ok(options.Select(o => new PlatformSettingAllowedValueDto
            {
                Value = o.Value,
                Label = o.Label,
                Description = o.Description
            }).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private async Task<bool> AuthorizeReadAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return false;

        var read = new PermissionDescriptor(
            ActionConstants.PlatformSettingsRead,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance));

        if (await _authorizationEvaluation.EvaluateAsync(
                new AuthorizationEvaluationContext(actor, read, read.Resource),
                cancellationToken))
            return true;

        return await AuthorizeManageAsync(cancellationToken);
    }

    private async Task<bool> AuthorizeManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return false;

        var permission = new PermissionDescriptor(
            ActionConstants.PlatformSettingsManage,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance));

        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, permission, permission.Resource),
            cancellationToken);
    }
}
