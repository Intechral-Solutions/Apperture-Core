using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>Operator email subscription preferences for platform notifications (S9).</summary>
[ApiController]
[Route("api/me/notification-subscriptions")]
[Authorize]
public sealed class MeNotificationSubscriptionsController : ControllerBase
{
    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IOperatorNotificationSubscriptionService _preferences;

    public MeNotificationSubscriptionsController(
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IOperatorNotificationSubscriptionService preferences)
    {
        _principalContext = principalContext;
        _principalToUserResolver = principalToUserResolver;
        _preferences = preferences;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PersonalNotificationEmailPreferenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PersonalNotificationEmailPreferenceDto>>> List(
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        var rows = await _preferences.ListEmailPreferencesAsync(userId, cancellationToken).ConfigureAwait(false);
        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpPut("{definitionId}")]
    [ProducesResponseType(typeof(PersonalNotificationEmailPreferenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PersonalNotificationEmailPreferenceDto>> SetEmailPreference(
        string definitionId,
        [FromBody] SetPersonalNotificationEmailPreferenceRequest body,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        if (string.IsNullOrWhiteSpace(definitionId))
            return BadRequest("definitionId is required.");

        try
        {
            var updated = await _preferences
                .SetEmailEnabledAsync(userId, definitionId.Trim(), body.EmailEnabled, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToDto(updated));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    private async Task<string?> ResolveUserIdAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return null;

        var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken).ConfigureAwait(false);
        return userId?.Value;
    }

    private static PersonalNotificationEmailPreferenceDto ToDto(OperatorEmailSubscriptionPreference preference) =>
        new()
        {
            DefinitionId = preference.DefinitionId,
            DisplayName = preference.DisplayName,
            Description = preference.Description,
            EmailEnabled = preference.EmailEnabled,
            HasSelfSubscription = preference.HasSelfSubscription,
            HasGlobalEmailOptOut = preference.HasGlobalEmailOptOut,
        };
}
