using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/notification-diagnostics")]
[Authorize]
public sealed class NotificationDiagnosticsController : ControllerBase
{
    private readonly INotificationDeliveryDiagnosticsService _diagnostics;

    public NotificationDiagnosticsController(INotificationDeliveryDiagnosticsService diagnostics) =>
        _diagnostics = diagnostics;

    [HttpGet("health")]
    [ProducesResponseType(typeof(NotificationDeliveryHealthSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationDeliveryHealthSummaryDto>> GetHealth(
        [FromQuery] string? definitionId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await _diagnostics
                .GetHealthSummaryAsync(definitionId, take, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToHealthDto(summary));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("by-integration/{integrationId}")]
    [ProducesResponseType(typeof(NotificationIntegrationDiagnosticsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationIntegrationDiagnosticsDto>> GetByIntegration(
        string integrationId,
        [FromQuery] string? definitionId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _diagnostics
                .GetByIntegrationAsync(integrationId, definitionId, take, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToIntegrationDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("by-incident/{attentionIncidentId}")]
    [ProducesResponseType(typeof(NotificationIncidentDiagnosticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationIncidentDiagnosticsDto>> GetByIncident(
        string attentionIncidentId,
        [FromQuery] string? definitionId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _diagnostics
                .GetByAttentionIncidentAsync(attentionIncidentId, definitionId, take, cancellationToken)
                .ConfigureAwait(false);
            return row is null ? NotFound() : Ok(ToIncidentDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("expected-run-missing/parity-acceptance")]
    [ProducesResponseType(typeof(ExpectedRunMissingParityAcceptanceSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpectedRunMissingParityAcceptanceSummaryDto>> GetExpectedRunMissingParityAcceptance(
        [FromQuery] string integrationId,
        [FromQuery] string? attentionIncidentId,
        [FromQuery] int maxIncidents = 10,
        [FromQuery] int takePerIncident = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _diagnostics
                .GetExpectedRunMissingParityAcceptanceAsync(
                    integrationId,
                    attentionIncidentId,
                    maxIncidents,
                    takePerIncident,
                    cancellationToken)
                .ConfigureAwait(false);
            return row is null ? NotFound() : Ok(ToAcceptanceDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("expected-run-missing/parity")]
    [ProducesResponseType(typeof(ExpectedRunMissingParityDiagnosticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpectedRunMissingParityDiagnosticsDto>> GetExpectedRunMissingParity(
        [FromQuery] string? integrationId,
        [FromQuery] string? attentionIncidentId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _diagnostics
                .GetExpectedRunMissingParityAsync(integrationId, attentionIncidentId, take, cancellationToken)
                .ConfigureAwait(false);
            return row is null ? NotFound() : Ok(ToParityDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static NotificationDeliveryHealthSummaryDto ToHealthDto(NotificationDeliveryHealthSummary s) =>
        new(
            s.NotificationDefinitionId,
            s.InstanceCountsByStatus.Select(c => new StatusCountDto(c.Status, c.Count)).ToList(),
            s.OutboxCountsByStatus.Select(c => new StatusCountDto(c.Status, c.Count)).ToList(),
            s.RecentFailedDeliveryCount,
            s.RecentSuppressedInstanceCount,
            s.RecentPendingOrDeliveringOutboxCount,
            s.GeneratedAtUtc);

    private static NotificationIntegrationDiagnosticsDto ToIntegrationDto(NotificationIntegrationDiagnostics d) =>
        new(
            d.IntegrationId,
            ToHealthDto(d.Health),
            d.RecentInstances.Select(ToInstanceDto).ToList(),
            d.RecentOutboxRows.Select(ToOutboxDto).ToList(),
            d.RecentSuppressedInstances.Select(ToInstanceDto).ToList(),
            d.RecentFailedDeliveries.Select(ToOutboxDto).ToList());

    private static NotificationIncidentDiagnosticsDto ToIncidentDto(NotificationIncidentDiagnostics d) =>
        new(
            d.AttentionIncidentId,
            d.IntegrationId,
            ToHealthDto(d.Health),
            d.Instances.Select(ToInstanceDto).ToList(),
            d.OutboxRows.Select(ToOutboxDto).ToList());

    private static ExpectedRunMissingParityDiagnosticsDto ToParityDto(ExpectedRunMissingParityDiagnostics p) =>
        new(
            p.IntegrationId,
            p.AttentionIncidentId,
            p.ByKind.Select(k => new ExpectedRunMissingKindParityDto(
                k.NotificationKind,
                k.DifferenceKind,
                k.Summary,
                k.Epic69Recipients,
                k.PlatformRecipients,
                k.RecipientComparisons.Select(r => new ParityRecipientComparisonDto(
                    r.RecipientEmail,
                    r.DifferenceKind,
                    r.Epic69Status,
                    r.Epic69SuppressionReason,
                    r.PlatformInstanceStatus,
                    r.PlatformOutboxStatus,
                    r.PlatformRecipientSource)).ToList(),
                k.Epic69AggregateStatus,
                k.PlatformInstanceStatus,
                k.PlatformInstanceSuppressionReason)).ToList(),
            p.PlatformInstances.Select(ToInstanceDto).ToList(),
            p.PlatformOutboxRows.Select(ToOutboxDto).ToList(),
            p.Epic69LedgerRows.Select(ToEpic69Dto).ToList(),
            p.GeneratedAtUtc);

    private static NotificationInstanceDiagnosticDto ToInstanceDto(NotificationInstanceDiagnosticRow i) =>
        new(
            i.Id,
            i.NotificationDefinitionId,
            i.NotificationKind,
            i.Status,
            i.IntegrationId,
            i.AttentionIncidentId,
            i.PlatformEventId,
            i.SuppressionReason,
            i.CreatedAtUtc,
            i.UpdatedAtUtc);

    private static NotificationOutboxDiagnosticDto ToOutboxDto(NotificationOutboxDiagnosticRow o) =>
        new(
            o.Id,
            o.NotificationInstanceId,
            o.NotificationDefinitionId,
            o.NotificationKind,
            o.IntegrationId,
            o.AttentionIncidentId,
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
            o.CreatedAtUtc,
            o.UpdatedAtUtc);

    private static ExpectedRunMissingParityAcceptanceSummaryDto ToAcceptanceDto(
        ExpectedRunMissingParityAcceptanceSummary s) =>
        new(
            s.IntegrationId,
            s.IncidentsEvaluated,
            s.KindComparisonsTotal,
            s.KindsAligned,
            s.KindsMissingInPlatform,
            s.KindsExtraInPlatform,
            s.KindsStatusMismatch,
            s.KindsDeliveryFailedInPlatform,
            s.KindsDeliverySucceededInPlatform,
            s.KindsSuppressedEpic69Only,
            s.KindsSuppressedPlatformOnly,
            s.RecipientComparisonsTotal,
            s.RecipientsAligned,
            s.RecipientsMissingInPlatform,
            s.RecipientsExtraInPlatform,
            s.RecipientsDeliveryFailedInPlatform,
            s.RecipientsPendingInPlatform,
            s.BlockingFindings,
            s.NonBlockingFindings,
            s.LooksAcceptableForCutover,
            s.GeneratedAtUtc);

    private static Epic69NotificationDiagnosticDto ToEpic69Dto(Epic69NotificationDiagnosticRow n) =>
        new(
            n.Id,
            n.AttentionIncidentId,
            n.IntegrationId,
            n.NotificationKind,
            n.Channel,
            n.Recipient,
            n.Status,
            n.SuppressionReason,
            n.CreatedAtUtc,
            n.SentAtUtc,
            n.RecipientResolvedFrom);
}
