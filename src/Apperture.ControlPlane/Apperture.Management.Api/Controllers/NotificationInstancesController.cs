using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/notification-instances")]
[Authorize]
public sealed class NotificationInstancesController : ControllerBase
{
    private readonly INotificationInstanceQueryService _instances;

    public NotificationInstancesController(INotificationInstanceQueryService instances) => _instances = instances;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationInstanceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationInstanceSummaryDto>>> List(
        [FromQuery] string? integrationId,
        [FromQuery] string? definitionId,
        [FromQuery] string? incidentId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _instances
                .ListAsync(
                    new NotificationInstanceListQuery(integrationId, definitionId, incidentId, take),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(rows.Select(ToSummaryDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(NotificationInstanceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationInstanceDetailDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _instances.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return row is null ? NotFound() : Ok(ToDetailDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}/outbox")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationDeliveryOutboxDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<NotificationDeliveryOutboxDto>>> ListOutbox(
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var instance = await _instances.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (instance is null)
                return NotFound();

            var rows = await _instances.ListOutboxAsync(id, cancellationToken).ConfigureAwait(false);
            return Ok(rows.Select(ToOutboxDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static NotificationInstanceSummaryDto ToSummaryDto(NotificationInstance i) =>
        new(
            i.Id.Value,
            i.NotificationDefinitionId,
            i.NotificationKind,
            i.Status,
            i.IntegrationId,
            i.AttentionIncidentId,
            i.PlatformEventId,
            i.CreatedAtUtc);

    private static NotificationInstanceDetailDto ToDetailDto(NotificationInstance i) =>
        new(
            i.Id.Value,
            i.NotificationDefinitionId,
            i.NotificationKind,
            i.Status,
            i.PlatformEventId,
            i.AttentionIncidentId,
            i.IntegrationId,
            i.ModuleId,
            i.RunId,
            i.ScheduleId,
            i.InstanceDedupeKey,
            i.RenderedSubject,
            i.RenderedSummary,
            i.RenderedBodyText,
            i.RenderedBodyHtml,
            i.RenderedActionText,
            i.RenderedActionUrl,
            i.RenderedContentFieldsJson,
            i.TemplateVersionHash,
            i.RenderContextJson,
            i.RecipientSnapshotJson,
            i.SuppressionReason,
            i.CreatedAtUtc,
            i.UpdatedAtUtc);

    private static NotificationDeliveryOutboxDto ToOutboxDto(NotificationDeliveryOutbox o) =>
        new(
            o.Id.Value,
            o.NotificationInstanceId.Value,
            o.RecipientEmail,
            o.RecipientDisplayName,
            o.RecipientKind,
            o.RecipientSource,
            o.Channel,
            o.Status,
            o.AttemptCount,
            o.NextAttemptAtUtc,
            o.LastError,
            o.SentAtUtc,
            o.CreatedAtUtc);
}
