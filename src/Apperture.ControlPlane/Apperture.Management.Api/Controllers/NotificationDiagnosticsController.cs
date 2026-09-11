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
    private readonly INotificationPipelineTestService _pipelineTests;

    public NotificationDiagnosticsController(
        INotificationDeliveryDiagnosticsService diagnostics,
        INotificationPipelineTestService pipelineTests)
    {
        _diagnostics = diagnostics;
        _pipelineTests = pipelineTests;
    }

    [HttpPost("smtp-test")]
    [ProducesResponseType(typeof(NotificationSmtpTestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationSmtpTestResultDto>> PostSmtpTest(
        [FromBody] NotificationSmtpTestRequestDto body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _pipelineTests
                .SendSmtpTestEmailAsync(new NotificationSmtpTestRequest(body.RecipientEmail), cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToSmtpTestDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("platform-pipeline-test")]
    [ProducesResponseType(typeof(PlatformNotificationPipelineTestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlatformNotificationPipelineTestResultDto>> PostPlatformPipelineTest(
        [FromBody] PlatformNotificationPipelineTestRequestDto body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _pipelineTests
                .RunPlatformPipelineTestAsync(
                    new PlatformNotificationPipelineTestRequest(
                        body.IntegrationId,
                        body.DefinitionId,
                        body.DryRun,
                        body.DispatchImmediately),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToPipelineTestDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("configuration")]
    [ProducesResponseType(typeof(NotificationConfigurationDiagnosticsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationConfigurationDiagnosticsDto>> GetConfiguration(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _diagnostics
                .GetConfigurationDiagnosticsAsync(cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToConfigurationDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

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

    private static NotificationConfigurationDiagnosticsDto ToConfigurationDto(NotificationConfigurationDiagnostics c) =>
        new(
            c.PlatformEventsEnabled,
            c.EvaluationEnabled,
            c.InstancesEnabled,
            c.DeliveryEnabled,
            c.Warnings.Select(w => new NotificationConfigurationSafetyWarningDto(w.Code, w.Message)).ToList(),
            c.GeneratedAtUtc);

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

    private static NotificationSmtpTestResultDto ToSmtpTestDto(NotificationSmtpTestResult r) =>
        new(
            r.Succeeded,
            r.FailureSummary,
            r.EmailTransportEnabled,
            r.SmtpHostConfigured,
            r.FromAddressConfigured,
            r.CredentialReferenceConfigured,
            r.GeneratedAtUtc);

    private static PlatformNotificationPipelineTestResultDto ToPipelineTestDto(PlatformNotificationPipelineTestResult r) =>
        new(
            r.DryRun,
            r.IntegrationId,
            r.DefinitionId,
            r.TestIncidentId,
            r.PlatformEventId,
            r.NotificationInstanceId,
            r.NotificationInstanceStatus,
            r.SuppressionReason,
            r.Stages.Select(s => new NotificationPipelineTestStageResultDto(s.Stage, s.Status, s.Detail)).ToList(),
            r.DeliverableRecipients.Select(ToRecipientDto).ToList(),
            r.SkippedRecipients.Select(ToRecipientDto).ToList(),
            r.OutboxRowsCreated,
            r.OutboxRowsSent,
            r.OutboxRowsFailed,
            r.OutboxRowsPending,
            r.DispatchNote,
            r.GeneratedAtUtc);

    private static NotificationPipelineTestRecipientEntryDto ToRecipientDto(NotificationPipelineTestRecipientEntry r) =>
        new(r.Email, r.DisplayName, r.Source, r.Reason);
}
