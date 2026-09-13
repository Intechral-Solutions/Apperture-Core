using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Domain.Notifications;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Authorize]
public sealed class NotificationSubscriptionsController : ControllerBase
{
    private readonly INotificationSubscriptionService _subscriptions;

    public NotificationSubscriptionsController(INotificationSubscriptionService subscriptions) =>
        _subscriptions = subscriptions;

    [HttpGet("api/notification-definitions/{definitionId}/subscriptions")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationSubscriptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationSubscriptionDto>>> ListByDefinition(
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _subscriptions.ListByDefinitionAsync(definitionId, cancellationToken).ConfigureAwait(false);
            return Ok(rows.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("api/integrations/{integrationId}/notification-subscriptions")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationSubscriptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationSubscriptionDto>>> ListByIntegration(
        string integrationId,
        [FromQuery] string? definitionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _subscriptions
                .ListByIntegrationAsync(integrationId, definitionId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(rows.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("api/notification-definitions/{definitionId}/subscriptions")]
    [ProducesResponseType(typeof(NotificationSubscriptionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationSubscriptionDto>> Add(
        string definitionId,
        [FromBody] AddNotificationSubscriptionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _subscriptions.AddRecipientAsync(
                    definitionId,
                    new NotificationSubscriptionAddRequest(
                        body.IntegrationId,
                        body.RecipientKind,
                        body.RecipientRef,
                        body.ManagedContactInput,
                        body.CreateManagedContactIfMissing,
                        body.DeliveryMode),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToDto(created));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("api/notification-definitions/{definitionId}/subscriptions/{subscriptionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(
        string definitionId,
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = await _subscriptions.RemoveAsync(definitionId, subscriptionId, cancellationToken)
                .ConfigureAwait(false);
            return removed ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPatch("api/notification-definitions/{definitionId}/subscriptions/{subscriptionId}")]
    [ProducesResponseType(typeof(NotificationSubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationSubscriptionDto>> Patch(
        string definitionId,
        string subscriptionId,
        [FromBody] PatchNotificationSubscriptionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _subscriptions.PatchAsync(
                    definitionId,
                    subscriptionId,
                    new NotificationSubscriptionPatchRequest(body.IsEnabled, body.DeliveryMode),
                    cancellationToken)
                .ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(ToDto(updated));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("api/notification-definitions/{definitionId}/recipients/resolve")]
    [ProducesResponseType(typeof(NotificationRecipientResolutionPreviewDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationRecipientResolutionPreviewDto>> Resolve(
        string definitionId,
        [FromBody] ResolveNotificationRecipientsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var preview = await _subscriptions.ResolveRecipientsPreviewAsync(
                    definitionId,
                    new NotificationRecipientResolveRequest(body.IntegrationId),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToPreviewDto(preview));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("api/notification-definitions/{definitionId}/user-opt-outs")]
    [ProducesResponseType(typeof(NotificationSubscriptionUserOverrideDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationSubscriptionUserOverrideDto>> SetUserOptOut(
        string definitionId,
        [FromBody] NotificationUserOptOutRequestDto body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _subscriptions.SetUserOptOutAsync(
                    definitionId,
                    new NotificationUserOptOutRequest(body.UserId, body.OperationalTeamId),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToOverrideDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("api/notification-definitions/{definitionId}/user-opt-outs")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveUserOptOut(
        string definitionId,
        [FromBody] NotificationUserOptOutRequestDto body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = await _subscriptions.RemoveUserOptOutAsync(
                    definitionId,
                    new NotificationUserOptOutRequest(body.UserId, body.OperationalTeamId),
                    cancellationToken)
                .ConfigureAwait(false);
            return removed ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static NotificationSubscriptionDto ToDto(NotificationSubscription s) =>
        new(
            s.Id.Value,
            s.NotificationDefinitionId,
            s.IntegrationId,
            s.RecipientKind,
            s.RecipientRef,
            s.DeliveryMode,
            s.Origin,
            s.IsEnabled,
            s.CreatedAtUtc,
            s.UpdatedAtUtc);

    private static NotificationSubscriptionUserOverrideDto ToOverrideDto(NotificationSubscriptionUserOverride o) =>
        new(o.Id, o.UserId, o.NotificationDefinitionId, o.OperationalTeamId, o.CreatedAtUtc);

    private static NotificationRecipientResolutionPreviewDto ToPreviewDto(NotificationRecipientResolutionPreview p) =>
        new(
            p.Deliverable.Select(r => new ResolvedNotificationRecipientDto(
                r.Email,
                r.DisplayName,
                r.UserId,
                r.ManagedContactId,
                r.Usability,
                r.Source,
                r.SubscriptionId,
                r.RecipientKind)).ToList(),
            p.Skipped.Select(s => new SkippedNotificationRecipientDto(
                s.Email,
                s.DisplayName,
                s.UserId,
                s.ManagedContactId,
                s.Source,
                s.SubscriptionId,
                s.RecipientKind,
                s.Reason)).ToList());
}
