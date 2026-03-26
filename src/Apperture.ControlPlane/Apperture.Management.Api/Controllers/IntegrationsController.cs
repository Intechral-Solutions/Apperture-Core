using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Integration CRUD API. Epic 49.
/// </summary>
[ApiController]
[Route("api/integrations")]
[Authorize]
public sealed class IntegrationsController : ControllerBase
{
    private readonly IIntegrationManagementService _integrationService;
    private readonly IIntegrationReadinessService _readinessService;
    private readonly IIntegrationScheduleDiagnosticsService _scheduleDiagnostics;
    private readonly IAttentionIncidentTracker _attentionIncidentTracker;
    private readonly IPrincipalContext _principalContext;
    private readonly TimeProvider _timeProvider;

    public IntegrationsController(
        IIntegrationManagementService integrationService,
        IIntegrationReadinessService readinessService,
        IIntegrationScheduleDiagnosticsService scheduleDiagnostics,
        IAttentionIncidentTracker attentionIncidentTracker,
        IPrincipalContext principalContext,
        TimeProvider timeProvider)
    {
        _integrationService = integrationService;
        _readinessService = readinessService;
        _scheduleDiagnostics = scheduleDiagnostics;
        _attentionIncidentTracker = attentionIncidentTracker;
        _principalContext = principalContext;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Lists integrations visible to the current actor.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IntegrationDto>>> List(CancellationToken cancellationToken)
    {
        try
        {
            var integrations = await _integrationService.ListIntegrationsAsync(cancellationToken);
            return Ok(integrations.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Fleet overview: integrations with readiness, blocking/advisory counts, verification signals, and attention tokens. Epic 61.
    /// </summary>
    [HttpGet("fleet")]
    public async Task<ActionResult<IReadOnlyList<Apperture.Management.Client.IntegrationFleetRowDto>>> Fleet(
        [FromQuery] string? state,
        [FromQuery] bool? runnable,
        [FromQuery] bool? hasBlocking,
        [FromQuery] bool? hasAdvisory,
        [FromQuery] bool? verificationConcern,
        [FromQuery] string? verification,
        [FromQuery] bool? hasOperationalFailure,
        [FromQuery] bool? hasUnexpectedSilence,
        [FromQuery] string? attention,
        [FromQuery] string? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string>? attentionTokens = null;
            if (!string.IsNullOrWhiteSpace(attention))
            {
                attentionTokens = attention.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            var query = new IntegrationFleetQuery(
                State: state,
                Runnable: runnable,
                HasBlocking: hasBlocking,
                HasAdvisory: hasAdvisory,
                VerificationConcern: verificationConcern,
                Verification: verification,
                HasOperationalFailure: hasOperationalFailure,
                HasUnexpectedSilence: hasUnexpectedSilence,
                AttentionTokens: attentionTokens,
                Sort: sort);
            var rows = await _readinessService.ListFleetAsync(query, cancellationToken);
            return Ok(rows.Select(ToFleetDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Evidence-based readiness: runnable flag and actionable issues from slots, bindings, connections, schedules.
    /// </summary>
    [HttpGet("{id}/readiness")]
    public async Task<ActionResult<IntegrationReadinessProjection>> GetReadiness(string id, CancellationToken cancellationToken)
    {
        try
        {
            var projection = await _readinessService.GetReadinessAsync(new IntegrationId(id), cancellationToken);
            if (projection is null)
                return NotFound();
            return Ok(projection);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Operator view: integration metadata, readiness, required slots, bindings, and schedules in one response.
    /// </summary>
    [HttpGet("{id}/detail")]
    public async Task<ActionResult<IntegrationOperatorDetailProjection>> GetDetail(string id, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _readinessService.GetOperatorDetailAsync(new IntegrationId(id), cancellationToken);
            if (detail is null)
                return NotFound();
            return Ok(detail);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Epic 67: schedule diagnostics and expected-fire evidence timeline.
    /// </summary>
    [HttpGet("{id}/schedule-diagnostics")]
    public async Task<ActionResult<Apperture.Management.Client.IntegrationScheduleDiagnosticsDto?>> GetScheduleDiagnostics(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var projection = await _scheduleDiagnostics.GetDiagnosticsAsync(new IntegrationId(id), cancellationToken);
            if (projection is null)
                return NotFound();
            return Ok(ToDiagnosticsDto(projection));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Epic 68: operator acknowledgement of a persisted attention incident (e.g. expected run missing).
    /// </summary>
    [HttpPost("{id}/attention-incidents/{incidentId}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAttentionIncident(
        string id,
        string incidentId,
        [FromBody] AcknowledgeAttentionIncidentRequestBody? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
            if (actor is null)
                return Unauthorized();

            var intId = new IntegrationId(id);
            var integration = await _integrationService.GetIntegrationAsync(intId, cancellationToken).ConfigureAwait(false);
            if (integration is null)
                return NotFound();

            var ok = await _attentionIncidentTracker.AcknowledgeAsync(
                    intId,
                    incidentId,
                    actor.Id.Value,
                    body?.Note,
                    _timeProvider.GetUtcNow(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!ok)
                return NotFound();
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Gets an integration by id.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<IntegrationDto>> Get(string id, CancellationToken cancellationToken)
    {
        try
        {
            var integration = await _integrationService.GetIntegrationAsync(new IntegrationId(id), cancellationToken);
            if (integration is null)
                return NotFound();
            return Ok(ToDto(integration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Creates a new integration.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<IntegrationDto>> Create([FromBody] CreateIntegrationRequestBody body, CancellationToken cancellationToken)
    {
        try
        {
            var request = new Apperture.Core.Application.Integration.CreateIntegrationRequest(body.DisplayName, body.ModuleId, body.ScopeKey, body.Settings);
            var integration = await _integrationService.CreateIntegrationAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = integration.Id.Value }, ToDto(integration));
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

    /// <summary>
    /// Updates integration metadata.
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<ActionResult<IntegrationDto>> Update(string id, [FromBody] UpdateIntegrationRequestBody body, CancellationToken cancellationToken)
    {
        try
        {
            IntegrationState? integrationState = null;
            if (body.State is not null && Enum.TryParse<IntegrationState>(body.State, ignoreCase: true, out var parsed))
                integrationState = parsed;
            var request = new Apperture.Core.Application.Integration.UpdateIntegrationRequest(body.DisplayName, integrationState, body.Settings);
            var integration = await _integrationService.UpdateIntegrationAsync(new IntegrationId(id), request, cancellationToken);
            return Ok(ToDto(integration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Activates an integration for operational execution.
    /// </summary>
    [HttpPost("{id}/activate")]
    public async Task<ActionResult<IntegrationDto>> Activate(string id, CancellationToken cancellationToken)
    {
        try
        {
            var integration = await _integrationService.ActivateIntegrationAsync(new IntegrationId(id), cancellationToken);
            return Ok(ToDto(integration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Deactivates an integration while preserving configuration.
    /// </summary>
    [HttpPost("{id}/deactivate")]
    public async Task<ActionResult<IntegrationDto>> Deactivate(string id, CancellationToken cancellationToken)
    {
        try
        {
            var integration = await _integrationService.DeactivateIntegrationAsync(new IntegrationId(id), cancellationToken);
            return Ok(ToDto(integration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Deletes an integration.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _integrationService.DeleteIntegrationAsync(new IntegrationId(id), cancellationToken);
            return NoContent();
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

    private static IntegrationDto ToDto(DomainIntegration i) => new(
        i.Id.Value,
        i.DisplayName,
        i.ModuleId,
        i.ScopeKey,
        i.State.ToString(),
        i.Settings,
        i.CreatedAt,
        i.UpdatedAt);

    private static Apperture.Management.Client.IntegrationFleetRowDto ToFleetDto(IntegrationFleetRowProjection r) =>
        new(
            r.Id,
            r.DisplayName,
            r.ModuleId,
            r.ScopeKey,
            r.OperationalState,
            r.CreatedAt,
            r.UpdatedAt,
            ToReadinessDto(r.Readiness),
            new Apperture.Management.Client.IntegrationFleetAttentionFlagsDto(
                r.Attention.IsInactiveOperational,
                r.Attention.IsRetired,
                r.Attention.HasConfigBlockingReadiness,
                r.Attention.HasAdvisoryOnly,
                r.Attention.HasVerificationFailed,
                r.Attention.HasVerificationUnverified,
                r.Attention.HasVerificationUnavailable),
            new Apperture.Management.Client.IntegrationFleetOperationalRunAttentionDto(
                r.OperationalRuns.RecentFailedRunCount,
                r.OperationalRuns.MostRecentFailedRunId,
                r.OperationalRuns.MostRecentFailedRunAt,
                r.OperationalRuns.LastAuthorizedRunId,
                r.OperationalRuns.LastAuthorizedRunStatus,
                r.OperationalRuns.LastAuthorizedRunAt,
                r.OperationalRuns.HasRecentOperationalFailure,
                r.OperationalRuns.HasRepeatedRecentFailures,
                r.OperationalRuns.MostRecentAuthorizedRunFailed,
                r.OperationalRuns.EvidenceWindowStartUtc),
            new Apperture.Management.Client.IntegrationFleetExpectedRunAttentionDto(
                r.ExpectedRuns.HasRecurringScheduleExpectation,
                r.ExpectedRuns.HasUnexpectedSilence,
                r.ExpectedRuns.ReferenceScheduleId,
                r.ExpectedRuns.ExpectationEvidenceWindowStartUtc,
                r.ExpectedRuns.LastScheduleOriginAuthorizedRunId,
                r.ExpectedRuns.LastScheduleOriginAuthorizedRunAt,
                r.ExpectedRuns.ExpectedRunEvaluationStatus,
                r.ExpectedRuns.LastExpectedFireUtc,
                r.ExpectedRuns.ExpectedFireCountInWindow,
                r.ExpectedRuns.ExpectedRunSatisfied,
                r.ExpectedRuns.ExpectedRunInGracePeriod,
                r.ExpectedRuns.OperatorSummaryLine,
                r.ExpectedRuns.ExpectedRunGraceReason,
                r.ExpectedRuns.ExpectedRunGraceAnchorUtc),
            r.AttentionTokens.ToArray(),
            r.TopIssueCodes.ToArray(),
            r.HasActiveExpectedRunMissingIncident);

    private static Apperture.Management.Client.IntegrationReadinessDto ToReadinessDto(IntegrationReadinessProjection p) =>
        new(
            p.IntegrationId,
            p.ModuleId,
            p.IsRunnable,
            p.BlockingIssueCount,
            p.AdvisoryIssueCount,
            p.Issues.Select(i => new Apperture.Management.Client.IntegrationReadinessIssueDto(
                i.Code,
                i.Message,
                i.SlotName,
                i.BindingId,
                i.ConnectionId,
                i.Severity)).ToArray());

    private static Apperture.Management.Client.IntegrationScheduleDiagnosticsDto ToDiagnosticsDto(
        IntegrationScheduleDiagnosticsProjection p) =>
        new(
            p.IntegrationId.Value,
            p.HasRecurringScheduleExpectation,
            p.EvaluationStatus.ToString(),
            p.InGracePeriod,
            p.GraceReason?.ToString(),
            p.GraceAnchorUtc,
            p.MostRecentExpectedFireUtc,
            p.LastSatisfyingScheduleOriginAuthorizedRunId,
            p.LastSatisfyingScheduleOriginAuthorizedRunAt,
            p.ReferenceScheduleId,
            p.ReferenceScheduleEnabled,
            p.LastScheduleEnabledAtUtc,
            p.LastScheduleDisabledAtUtc,
            p.MaterialScheduleSemanticsChangedAtUtc,
            p.LastAuthorizedRunId,
            p.LastAuthorizedRunStatus,
            p.LastAuthorizedRunAt,
            p.RecentExpectedFires.Select(e => new Apperture.Management.Client.ExpectedFireDiagnosticDto(
                e.ExpectedFireUtc,
                e.Outcome.ToString(),
                e.SatisfyingScheduleOriginAuthorizedRunId,
                e.SatisfyingScheduleOriginAuthorizedRunAt,
                e.SuppressedByGraceReason?.ToString())).ToList(),
            p.RecentAttentionIncidents.Select(i => new Apperture.Management.Client.AttentionIncidentDto(
                i.Id,
                i.IntegrationId,
                i.ConditionKey,
                i.Status,
                i.StartedAtUtc,
                i.LastObservedAtUtc,
                i.ResolvedAtUtc,
                i.ResolutionReason,
                i.FirstExpectedFireAtUtc,
                i.LastExpectedFireAtUtc,
                i.AcknowledgedAtUtc,
                i.AcknowledgedBy,
                i.AcknowledgementNote)).ToList(),
            p.RecentAttentionIncidentNotifications.Select(n => new Apperture.Management.Client.AttentionIncidentNotificationDto(
                n.Id,
                n.AttentionIncidentId,
                n.IntegrationId,
                n.ConditionKey,
                n.NotificationKind,
                n.Channel,
                n.Recipient,
                n.Status,
                n.SuppressionReason,
                n.CreatedAtUtc,
                n.AttemptedAtUtc,
                n.SentAtUtc,
                n.FailedAtUtc,
                n.FailureSummary,
                n.ProviderMessageId,
                n.SubjectSnapshot)).ToList());
}

public sealed record IntegrationDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string State,
    IReadOnlyDictionary<string, string>? Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateIntegrationRequestBody(
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    IReadOnlyDictionary<string, string>? Settings = null);

public sealed record UpdateIntegrationRequestBody(
    string? DisplayName,
    string? State,
    IReadOnlyDictionary<string, string>? Settings = null);

public sealed record AcknowledgeAttentionIncidentRequestBody(string? Note);
