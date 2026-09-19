using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Api.Models;
using Apperture.Management.Client;
using Client = Apperture.Management.Client;
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
    private readonly IIntegrationConfigurationRegistry _configurationRegistry;
    private readonly IIntegrationScheduleDiagnosticsService _scheduleDiagnostics;
    private readonly IAttentionIncidentTracker _attentionIncidentTracker;
    private readonly IAttentionRoutingManagementService _attentionRoutingManagement;
    private readonly IPrincipalContext _principalContext;
    private readonly TimeProvider _timeProvider;
    private readonly IResourceContextService _resourceContexts;
    private readonly IResourceOwnershipMoveService _moveService;

    public IntegrationsController(
        IIntegrationManagementService integrationService,
        IIntegrationReadinessService readinessService,
        IIntegrationConfigurationRegistry configurationRegistry,
        IIntegrationScheduleDiagnosticsService scheduleDiagnostics,
        IAttentionIncidentTracker attentionIncidentTracker,
        IAttentionRoutingManagementService attentionRoutingManagement,
        IPrincipalContext principalContext,
        TimeProvider timeProvider,
        IResourceContextService resourceContexts,
        IResourceOwnershipMoveService moveService)
    {
        _integrationService = integrationService;
        _readinessService = readinessService;
        _configurationRegistry = configurationRegistry;
        _scheduleDiagnostics = scheduleDiagnostics;
        _attentionIncidentTracker = attentionIncidentTracker;
        _attentionRoutingManagement = attentionRoutingManagement;
        _principalContext = principalContext;
        _timeProvider = timeProvider;
        _resourceContexts = resourceContexts;
        _moveService = moveService;
    }

    /// <summary>
    /// Lists integrations visible to the current actor.
    /// </summary>
    /// <param name="contextId">
    /// Optional owning ResourceContext filter. Omitted (the default) returns the same global set this
    /// endpoint has always returned. An unknown ResourceContext is rejected with 400 rather than
    /// silently returning nothing.
    /// </param>
    /// <param name="moduleId">
    /// Optional Module filter (matches <c>Integration.ModuleId</c>). Composes with <paramref name="contextId"/>
    /// (AND) when both are supplied. Omitted (the default) applies no Module filter.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IntegrationDto>>> List(
        [FromQuery] string? contextId,
        [FromQuery] string? moduleId,
        CancellationToken cancellationToken)
    {
        try
        {
            var (contextFilter, contextError) = await ResolveContextFilterAsync(contextId, cancellationToken);
            if (contextError is not null)
                return BadRequest(new { error = contextError });

            var integrations = await _integrationService.ListIntegrationsAsync(contextFilter, moduleId, cancellationToken);
            var contexts = await _resourceContexts.ListAsync(includeArchived: true, cancellationToken);
            return Ok(integrations.Select(i => ToDto(i, contexts)).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Resolves an optional <c>contextId</c> query value. Returns a null filter when absent.
    /// </summary>
    private async Task<(ResourceContextId? Filter, string? Error)> ResolveContextFilterAsync(
        string? contextId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contextId))
            return (null, null);

        var id = new ResourceContextId(contextId);
        var resolved = await _resourceContexts.GetAsync(id, cancellationToken);
        return resolved is null
            ? (null, $"ResourceContext '{contextId}' not found.")
            : (id, null);
    }

    /// <summary>
    /// Fleet overview: integrations with readiness, blocking/advisory counts, verification signals, and attention tokens. Epic 61.
    /// </summary>
    [HttpGet("fleet")]
    public async Task<ActionResult<IReadOnlyList<Apperture.Management.Client.IntegrationFleetRowDto>>> Fleet(
        [FromQuery] string? contextId,
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
            var (contextFilter, contextError) = await ResolveContextFilterAsync(contextId, cancellationToken);
            if (contextError is not null)
                return BadRequest(new { error = contextError });

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
                Sort: sort,
                ResourceContextId: contextFilter?.Value);
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
    /// SDK-6C: module-authored setting and connection requirement declarations for this integration's module.
    /// </summary>
    [HttpGet("{id}/configuration/declarations")]
    public async Task<ActionResult<Models.IntegrationConfigurationDeclarationsDto>> GetConfigurationDeclarations(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var integration = await _integrationService.GetIntegrationAsync(new IntegrationId(id), cancellationToken);
            if (integration is null)
                return NotFound();

            if (!_configurationRegistry.TryGetModuleConfiguration(integration.ModuleId, out var configuration))
                return NotFound(new { error = $"Module '{integration.ModuleId}' has no SDK configuration declarations." });

            return Ok(IntegrationConfigurationDtoMapper.ToDto(configuration));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// SDK-6C: procedure-scoped readiness (requirement subset when declared).
    /// </summary>
    [HttpGet("{id}/procedures/{procedureId}/readiness")]
    public async Task<ActionResult<Models.IntegrationProcedureReadinessDto>> GetProcedureReadiness(
        string id,
        string procedureId,
        CancellationToken cancellationToken)
    {
        try
        {
            var projection = await _readinessService.GetProcedureReadinessAsync(new IntegrationId(id), procedureId, cancellationToken);
            if (projection is null)
                return NotFound();
            return Ok(IntegrationConfigurationDtoMapper.ToProcedureReadinessDto(projection));
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
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "Integration id is required." });

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
    public async Task<ActionResult<IntegrationScheduleDiagnosticsDto?>> GetScheduleDiagnostics(
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
    /// Epic 70: effective attention routing (recipient resolution + policy) for an integration condition.
    /// </summary>
    [HttpGet("{id}/attention-routing")]
    public async Task<ActionResult<AttentionRoutingEffectiveDto>> GetAttentionRouting(
        string id,
        [FromQuery] string? conditionKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var intId = new IntegrationId(id);
            if (await _integrationService.GetIntegrationAsync(intId, cancellationToken).ConfigureAwait(false) is null)
                return NotFound();

            var key = string.IsNullOrWhiteSpace(conditionKey)
                ? AttentionConditionKeys.ExpectedRunMissing
                : conditionKey.Trim();
            var r = await _attentionRoutingManagement
                .GetEffectiveRoutingAsync(intId, key, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToAttentionRoutingDto(r));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Epic 70: upsert integration-level attention routing override for a condition.
    /// </summary>
    [HttpPut("{id}/attention-routing")]
    public async Task<ActionResult<AttentionRoutingEffectiveDto>> PutAttentionRouting(
        string id,
        [FromBody] UpsertAttentionRoutingRequest body,
        [FromQuery] string? conditionKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var intId = new IntegrationId(id);
            if (await _integrationService.GetIntegrationAsync(intId, cancellationToken).ConfigureAwait(false) is null)
                return NotFound();

            var key = string.IsNullOrWhiteSpace(conditionKey)
                ? AttentionConditionKeys.ExpectedRunMissing
                : conditionKey.Trim();
            try
            {
                await _attentionRoutingManagement.UpsertIntegrationRoutingAsync(
                        intId,
                        key,
                        new AttentionRoutingUpsertCommand(
                            body.IsEnabled,
                            body.SendResolvedNotification,
                            body.SuppressRemindersWhenAcknowledged,
                            body.SuppressEscalationWhenAcknowledged,
                            body.EscalationAfterTicks is { } et ? TimeSpan.FromTicks(et) : null,
                            body.ReminderIntervalTicks is { } rt ? TimeSpan.FromTicks(rt) : null,
                            body.PrimaryOperationalTeamId,
                            body.EscalationOperationalTeamId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }

            var r = await _attentionRoutingManagement
                .GetEffectiveRoutingAsync(intId, key, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToAttentionRoutingDto(r));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// CTX-3B: clears an integration-level attention routing override, reverting to the instance default.
    /// </summary>
    [HttpDelete("{id}/attention-routing")]
    public async Task<IActionResult> DeleteAttentionRouting(
        string id, [FromQuery] string? conditionKey, CancellationToken cancellationToken)
    {
        try
        {
            var intId = new IntegrationId(id);
            if (await _integrationService.GetIntegrationAsync(intId, cancellationToken).ConfigureAwait(false) is null)
                return NotFound();

            var key = string.IsNullOrWhiteSpace(conditionKey)
                ? AttentionConditionKeys.ExpectedRunMissing
                : conditionKey.Trim();
            try
            {
                await _attentionRoutingManagement
                    .ClearIntegrationRoutingAsync(intId, key, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }

            return NoContent();
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
            return Ok(await ToDtoAsync(integration, cancellationToken));
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
            var request = new Apperture.Core.Application.Integration.CreateIntegrationRequest(
                body.DisplayName,
                body.ModuleId,
                body.ScopeKey,
                body.Settings,
                string.IsNullOrWhiteSpace(body.ResourceContextId) ? null : new ResourceContextId(body.ResourceContextId));
            var integration = await _integrationService.CreateIntegrationAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = integration.Id.Value }, await ToDtoAsync(integration, cancellationToken));
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
            return Ok(await ToDtoAsync(integration, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ResourceContextArchivedException ex)
        {
            return Conflict(new { error = ex.Message });
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
            return Ok(await ToDtoAsync(integration, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ResourceContextArchivedException ex)
        {
            return Conflict(new { error = ex.Message });
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
            return Ok(await ToDtoAsync(integration, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ResourceContextArchivedException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Retires an integration: deliberately removed from active service and retained for history.
    /// </summary>
    [HttpPost("{id}/retire")]
    public async Task<ActionResult<IntegrationDto>> Retire(string id, CancellationToken cancellationToken)
    {
        try
        {
            var integration = await _integrationService.RetireIntegrationAsync(new IntegrationId(id), cancellationToken);
            return Ok(await ToDtoAsync(integration, cancellationToken));
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

    /// <summary>Advisory preview of moving this Integration to another ResourceContext (design gate §8.2).</summary>
    [HttpGet("{id}/ownership/move-preview")]
    public async Task<IActionResult> GetOwnershipMovePreview(
        string id, [FromQuery] string targetResourceContextId, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _moveService.PreviewIntegrationMoveAsync(
                new IntegrationId(id), new ResourceContextId(targetResourceContextId), cancellationToken);
            return Ok(ResourceMoveHttpMapper.ToDto(preview));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>Moves this Integration to another ResourceContext. Re-validates identically inside the gated transaction (§8.2).</summary>
    [HttpPost("{id}/ownership/move")]
    public async Task<IActionResult> PostOwnershipMove(
        string id, [FromBody] Client.ResourceOwnershipMoveRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _moveService.MoveIntegrationAsync(
                new IntegrationId(id), new ResourceContextId(body.TargetResourceContextId), cancellationToken);
            return ResourceMoveHttpMapper.MapMoveResult(preview);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ResourceOwnershipBusyException ex) { return ResourceMoveHttpMapper.MapBusy(ex); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>
    /// Maps an Integration to its DTO, resolving the ResourceContext reference from a pre-fetched set.
    /// </summary>
    /// <remarks>
    /// The caller passes the whole (small) ResourceContext dimension so mapping a list costs one query
    /// rather than one query per row.
    /// </remarks>
    private static IntegrationDto ToDto(DomainIntegration i, IReadOnlyList<ResourceContext> resourceContexts)
    {
        var owning = resourceContexts.FirstOrDefault(c => string.Equals(c.Id.Value, i.ResourceContextId.Value, StringComparison.Ordinal));
        return new IntegrationDto(
            i.Id.Value,
            i.DisplayName,
            i.ModuleId,
            i.ScopeKey,
            i.State.ToString(),
            i.Settings,
            i.CreatedAt,
            i.UpdatedAt,
            i.ResourceContextId.Value,
            owning?.ToRef());
    }

    private async Task<IntegrationDto> ToDtoAsync(DomainIntegration i, CancellationToken cancellationToken)
    {
        var contexts = await _resourceContexts.ListAsync(includeArchived: true, cancellationToken);
        return ToDto(i, contexts);
    }

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
            r.HasActiveExpectedRunMissingIncident,
            r.ResourceContextId);

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

    private static IntegrationScheduleDiagnosticsDto ToDiagnosticsDto(
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
            p.RecentExpectedFires.Select(e => new ExpectedFireDiagnosticDto(
                e.ExpectedFireUtc,
                e.Outcome.ToString(),
                e.SatisfyingScheduleOriginAuthorizedRunId,
                e.SatisfyingScheduleOriginAuthorizedRunAt,
                e.SuppressedByGraceReason?.ToString())).ToList(),
            p.RecentAttentionIncidents.Select(i => new AttentionIncidentDto(
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
                i.AcknowledgementNote,
                i.ResourceContextId)).ToList(),
            ToAttentionRoutingDto(p.AttentionRouting),
            p.RecentPlatformNotificationInstances.Select(i => new PlatformNotificationInstanceDiagnosticDto(
                i.Id,
                i.NotificationDefinitionId,
                i.NotificationKind,
                i.Status,
                i.AttentionIncidentId,
                i.SuppressionReason,
                i.CreatedAtUtc)).ToList(),
            p.RecentPlatformNotificationOutbox.Select(o => new PlatformNotificationOutboxDiagnosticDto(
                o.Id,
                o.NotificationInstanceId,
                o.NotificationKind,
                o.AttentionIncidentId,
                o.RecipientEmail,
                o.RecipientSource,
                o.Status,
                o.LastError,
                o.SentAtUtc,
                o.UpdatedAtUtc)).ToList());

    private static AttentionRoutingEffectiveDto ToAttentionRoutingDto(AttentionRecipientResolution r) =>
        new(
            r.HasRoute,
            r.Enabled,
            r.PrimaryRecipients.ToList(),
            r.PrimarySource,
            r.EscalationRecipients.ToList(),
            r.EscalationSource,
            r.PrimaryOperationalTeamId,
            r.PrimaryOperationalTeamName,
            r.EscalationOperationalTeamId,
            r.EscalationOperationalTeamName,
            r.HasPrimaryRoute,
            r.HasEscalationRoute,
            r.PrimaryNoRouteReason,
            r.EscalationNoRouteReason,
            r.ResolvedPrimaryPrincipalUserIds.ToList(),
            r.ResolvedEscalationPrincipalUserIds.ToList(),
            r.ResolvedPrimaryExternalRecipients.ToList(),
            r.ResolvedEscalationExternalRecipients.ToList(),
            r.EscalationAfter?.Ticks,
            r.ReminderInterval?.Ticks,
            r.SendResolvedNotification,
            r.SuppressRemindersWhenAcknowledged,
            r.SuppressEscalationWhenAcknowledged,
            r.IntegrationRowPrimaryOperationalTeamId,
            r.IntegrationRowEscalationOperationalTeamId);

    private static AttentionRoutingEffectiveDto ToAttentionRoutingDto(AttentionRoutingDiagnosticsSummary s) =>
        new(
            s.HasRoute,
            s.Enabled,
            s.PrimaryRecipients.ToList(),
            s.PrimarySource,
            s.EscalationRecipients.ToList(),
            s.EscalationSource,
            s.PrimaryOperationalTeamId,
            s.PrimaryOperationalTeamName,
            s.EscalationOperationalTeamId,
            s.EscalationOperationalTeamName,
            s.HasPrimaryRoute,
            s.HasEscalationRoute,
            s.PrimaryNoRouteReason,
            s.EscalationNoRouteReason,
            s.ResolvedPrimaryPrincipalUserIds.ToList(),
            s.ResolvedEscalationPrincipalUserIds.ToList(),
            s.ResolvedPrimaryExternalRecipients.ToList(),
            s.ResolvedEscalationExternalRecipients.ToList(),
            s.EscalationAfter?.Ticks,
            s.ReminderInterval?.Ticks,
            s.SendResolvedNotification,
            s.SuppressRemindersWhenAcknowledged,
            s.SuppressEscalationWhenAcknowledged,
            s.IntegrationRowPrimaryOperationalTeamId,
            s.IntegrationRowEscalationOperationalTeamId);
}

/// <param name="ScopeKey">Authorization scope. Unrelated to <paramref name="ResourceContextId"/>.</param>
/// <param name="ResourceContextId">Owning ResourceContext id.</param>
/// <param name="ResourceContext">Owning ResourceContext reference for display.</param>
public sealed record IntegrationDto(
    string Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    string State,
    IReadOnlyDictionary<string, string>? Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ResourceContextId = null,
    ResourceContextRef? ResourceContext = null);

/// <param name="ResourceContextId">
/// Optional owning ResourceContext. Omitted means the reserved Shared context, so existing clients
/// remain compatible. An unknown or archived context is rejected.
/// </param>
public sealed record CreateIntegrationRequestBody(
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    IReadOnlyDictionary<string, string>? Settings = null,
    string? ResourceContextId = null);

public sealed record UpdateIntegrationRequestBody(
    string? DisplayName,
    string? State,
    IReadOnlyDictionary<string, string>? Settings = null);

public sealed record AcknowledgeAttentionIncidentRequestBody(string? Note);
