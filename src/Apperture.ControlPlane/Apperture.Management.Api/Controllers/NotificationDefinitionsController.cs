using Apperture.Core.Application.Notifications;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/notification-definitions")]
[Authorize]
public sealed class NotificationDefinitionsController : ControllerBase
{
    private readonly INotificationDefinitionService _definitions;

    public NotificationDefinitionsController(INotificationDefinitionService definitions) =>
        _definitions = definitions;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationDefinitionSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationDefinitionSummaryDto>>> List(
        [FromQuery] string? integrationId,
        [FromQuery] string? eventType,
        [FromQuery] bool? enabled,
        [FromQuery] string? origin,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _definitions
                .ListAsync(
                    new NotificationDefinitionListQuery(integrationId, eventType, enabled, origin, take),
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
    [ProducesResponseType(typeof(NotificationDefinitionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDefinitionDetailDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _definitions.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return row is null ? NotFound() : Ok(ToDetailDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(NotificationDefinitionDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationDefinitionDetailDto>> Create(
        [FromBody] CreateNotificationDefinitionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _definitions.CreateAsync(
                    new NotificationDefinitionCreateRequest(
                        body.DisplayName,
                        body.Description,
                        body.IntegrationId,
                        body.RuleJson,
                        body.TemplateJson,
                        body.IsEnabled,
                        body.DefaultSeverity),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToDetailDto(created));
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

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(NotificationDefinitionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDefinitionDetailDto>> Update(
        string id,
        [FromBody] UpdateNotificationDefinitionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _definitions.UpdateAsync(
                    id,
                    new NotificationDefinitionUpdateRequest(
                        body.DisplayName,
                        body.Description,
                        body.RuleJson,
                        body.TemplateJson,
                        body.IsEnabled,
                        body.DefaultSeverity),
                    cancellationToken)
                .ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(ToDetailDto(updated));
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

    [HttpPatch("{id}/enabled")]
    [ProducesResponseType(typeof(NotificationDefinitionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDefinitionDetailDto>> SetEnabled(
        string id,
        [FromBody] SetNotificationDefinitionEnabledRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _definitions.SetEnabledAsync(id, body.IsEnabled, cancellationToken).ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(ToDetailDto(updated));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("validate")]
    [ProducesResponseType(typeof(NotificationValidationResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationValidationResultDto>> Validate(
        [FromBody] ValidateNotificationDefinitionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _definitions.ValidateAsync(
                    new NotificationDefinitionValidateRequest(
                        body.RuleJson,
                        body.TemplateJson,
                        body.Origin,
                        body.OwnerIntegrationId,
                        body.EventTypes),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToValidationDto(result));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("{id}/render-preview")]
    [ProducesResponseType(typeof(RenderedNotificationSnapshotDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RenderedNotificationSnapshotDto>> RenderPreview(
        string id,
        [FromBody] RenderNotificationPreviewRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _definitions.RenderPreviewAsync(
                    id,
                    new NotificationDefinitionRenderPreviewRequest(
                        body.TemplateJson,
                        body.VariantKey,
                        ToSample(body.Sample)),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToSnapshotDto(snapshot));
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

    private static NotificationDefinitionSummaryDto ToSummaryDto(Core.Domain.Notifications.NotificationDefinition d) =>
        new(
            d.Id.Value,
            d.DisplayName,
            d.Description,
            d.SourceKind,
            d.Origin,
            d.Scope,
            d.IntegrationId,
            d.PrimaryEventType,
            d.IsEnabled,
            d.DefaultSeverity,
            d.UpdatedAtUtc);

    private static NotificationDefinitionDetailDto ToDetailDto(Core.Domain.Notifications.NotificationDefinition d) =>
        new(
            d.Id.Value,
            d.DisplayName,
            d.Description,
            d.SourceKind,
            d.Origin,
            d.Scope,
            d.Mutability,
            d.ModuleId,
            d.IntegrationId,
            d.PrimaryEventType,
            d.IsEnabled,
            d.DefaultSeverity,
            d.RuleJson,
            d.TemplateJson,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);

    private static NotificationValidationResultDto ToValidationDto(NotificationValidationResult r) =>
        new(
            r.IsValid,
            r.Issues.Select(i => new NotificationValidationIssueDto(i.Code, i.Message, i.Path)).ToList());

    private static RenderedNotificationSnapshotDto ToSnapshotDto(RenderedNotificationSnapshot s) =>
        new(s.Subject, s.Summary, s.BodyText, s.BodyHtml, s.ActionText, s.ActionUrl, s.TemplateVersionHash);

    private static NotificationRenderSample ToSample(NotificationRenderSampleDto dto) =>
        new(
            dto.EventType,
            dto.EventTimeUtc,
            dto.Severity,
            dto.IntegrationId,
            dto.IntegrationDisplayName,
            dto.NotificationKind,
            dto.IncidentId,
            dto.IncidentStartedAtUtc,
            dto.InstanceBaseUrl,
            dto.EventData);
}
