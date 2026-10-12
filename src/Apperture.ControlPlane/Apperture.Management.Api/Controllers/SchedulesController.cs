using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Client = Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/schedules")]
[Authorize]
public sealed class SchedulesController : ControllerBase
{
    private const string LegacyScheduleWarningHeader =
        "299 - \"Legacy schedules without IntegrationId detected; migrate schedules to integration-owned records.\"";
    private readonly IScheduleGovernanceService _scheduleGovernance;
    private readonly IScheduleManagementService _scheduleManagement;
    private readonly IScheduleValidationService _scheduleValidation;
    private readonly IScheduleCreateTimeZoneDefaults _createTimeZoneDefaults;
    private readonly ISchedulerStatusQueryService _schedulerStatus;
    private readonly IResourceContextService _resourceContexts;
    private readonly IResourceOwnershipMoveService _moveService;

    public SchedulesController(
        IScheduleGovernanceService scheduleGovernance,
        IScheduleManagementService scheduleManagement,
        IScheduleValidationService scheduleValidation,
        IScheduleCreateTimeZoneDefaults createTimeZoneDefaults,
        ISchedulerStatusQueryService schedulerStatus,
        IResourceContextService resourceContexts,
        IResourceOwnershipMoveService moveService)
    {
        _scheduleGovernance = scheduleGovernance;
        _scheduleManagement = scheduleManagement;
        _scheduleValidation = scheduleValidation;
        _createTimeZoneDefaults = createTimeZoneDefaults;
        _schedulerStatus = schedulerStatus;
        _resourceContexts = resourceContexts;
        _moveService = moveService;
    }

    [HttpGet("capabilities")]
    [ProducesResponseType(typeof(ScheduleCapabilitiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ScheduleCapabilitiesDto>> GetCapabilities(CancellationToken cancellationToken = default)
    {
        var canManage = await _scheduleGovernance.CanManageAsync(cancellationToken);
        if (!canManage)
            return Forbid();
        return Ok(new ScheduleCapabilitiesDto(true));
    }

    /// <param name="contextId">
    /// Optional owning ResourceContext filter. Omitted (the default) returns the same global set this
    /// endpoint has always returned. An unknown ResourceContext is rejected with 400.
    /// </param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ScheduleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ScheduleDto>>> List(
        [FromQuery] string? contextId,
        CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();

        ResourceContextId? contextFilter = null;
        if (!string.IsNullOrWhiteSpace(contextId))
        {
            contextFilter = new ResourceContextId(contextId);
            if (await _resourceContexts.GetAsync(contextFilter, cancellationToken) is null)
                return BadRequest(new { errors = new[] { $"ResourceContext '{contextId}' not found." } });
        }

        var definitions = await _scheduleManagement.ListDefinitionsAsync(contextFilter, cancellationToken);
        if (definitions.Any(d => d.IntegrationId is null))
            TryAppendWarningHeader(LegacyScheduleWarningHeader);

        var asOf = DateTimeOffset.UtcNow;
        var workerStatus = await TryGetWorkerRuntimeStatusAsync(cancellationToken);
        return Ok(definitions.Select(d => ToDto(d, asOf, workerStatus)).ToList());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ScheduleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScheduleDetailDto>> Get(string id, [FromQuery] string? runtimePlaneId, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        var planeId = new RuntimePlaneId(runtimePlaneId ?? "local");
        var detail = await _scheduleManagement.GetScheduleWithExecutionStateAsync(new ScheduleId(id), planeId, cancellationToken);
        if (detail is null)
            return NotFound();
        if (detail.Definition.IntegrationId is null)
            TryAppendWarningHeader(LegacyScheduleWarningHeader);

        var workerStatus = await TryGetWorkerRuntimeStatusAsync(cancellationToken);
        var owningContext = detail.Definition.ResourceContextId is { } ownerId
            ? await _resourceContexts.GetAsync(ownerId, cancellationToken)
            : null;
        return Ok(ToDetailDto(detail, planeId.Value, DateTimeOffset.UtcNow, workerStatus, owningContext));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScheduleDto>> Create([FromBody] CreateScheduleRequestBody body, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        if (string.IsNullOrWhiteSpace(body.IntegrationId))
        {
            return BadRequest(new
            {
                errors = new[]
                {
                    "IntegrationId is required. Create schedules under POST /api/integrations/{integrationId}/schedules so the schedule is owned by an integration."
                }
            });
        }

        var timeZone = await _createTimeZoneDefaults.ResolveCreateTimeZoneIdAsync(body.TimeZone, cancellationToken);
        var validation = _scheduleValidation.ValidateCreate(
            body.Name,
            body.RecurrenceExpression,
            timeZone,
            body.Enabled,
            body.TargetJobType,
            body.TargetJobConfiguration);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors });
        var jobType = (ScheduledJobType)(body.TargetJobType ?? 0);
        var id = Guid.NewGuid().ToString("N")[..24];
        var target = new ScheduledTarget(jobType, body.TargetJobConfiguration!);
        var integrationId = new IntegrationId(body.IntegrationId!);
        var definition = new ScheduleDefinition(
            new ScheduleId(id),
            body.Name,
            body.RecurrenceExpression,
            timeZone,
            body.Enabled,
            target,
            body.ExecutionPolicyRef,
            Version: 1,
            integrationId)
        {
            ResourceContextId = string.IsNullOrWhiteSpace(body.ResourceContextId)
                ? null
                : new ResourceContextId(body.ResourceContextId)
        };

        var planeId = new RuntimePlaneId(body.RuntimePlaneId ?? "local");
        ScheduleDefinition created;
        try
        {
            // The management service owns the authoritative ownership rule (inherit from the
            // Integration; reject a mismatch). The controller only forwards the caller's request.
            // The definition and its initial assignment are one operation: neither exists without the other.
            created = await _scheduleManagement.CreateScheduleWithAssignmentAsync(definition, planeId, null, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex is not AuditInvariantException)
        {
            return BadRequest(new { errors = new[] { ex.Message } });
        }

        return CreatedAtAction(nameof(Get), new { id }, ToDto(created, DateTimeOffset.UtcNow, workerRuntimeStatus: null));
    }

    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScheduleDto>> Update(string id, [FromBody] UpdateScheduleRequestBody body, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        if (body.TargetJobType is not null || body.TargetJobConfiguration is not null)
            return BadRequest(new { errors = new[] { "Target cannot be changed after creation. TargetJobType and TargetJobConfiguration are immutable." } });
        var validation = _scheduleValidation.ValidateUpdate(body.Name, body.RecurrenceExpression, body.TimeZone, body.Enabled);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors });
        // Only the fields the body carries are applied, over the schedule as the service locks it; nothing is read here first,
        // so an omitted field can never be restored from an earlier read. The response is the schedule as committed.
        ScheduleDefinition committed;
        try
        {
            committed = await _scheduleManagement.PatchScheduleAsync(
                new ScheduleId(id),
                new SchedulePatch(body.Name, body.RecurrenceExpression, body.TimeZone, body.Enabled),
                cancellationToken);
        }
        catch (ScheduleNotFoundException)
        {
            return NotFound();
        }
        catch (ResourceContextArchivedException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex is not AuditInvariantException)
        {
            return BadRequest(new { error = ex.Message });
        }
        return Ok(ToDto(committed, DateTimeOffset.UtcNow, workerRuntimeStatus: null));
    }

    [HttpPost("{id}/enable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Enable(string id, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        try
        {
            await _scheduleManagement.EnableAsync(new ScheduleId(id), cancellationToken);
            return Ok();
        }
        catch (ResourceContextArchivedException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex is not AuditInvariantException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id}/disable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Disable(string id, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        try
        {
            await _scheduleManagement.DisableAsync(new ScheduleId(id), cancellationToken);
            return Ok();
        }
        catch (InvalidOperationException ex) when (ex is not AuditInvariantException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Hard-deletes a disabled schedule. Enabled schedules are refused. Historical firings are retained. The delete and its
    /// audit row commit together; a commit that could not be confirmed returns 500 <c>outcome_unknown</c>.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        try
        {
            await _scheduleManagement.DeleteScheduleAsync(new ScheduleId(id), cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ScheduleDeleteRejectedException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ScheduleNotFoundException)
        {
            // Only a genuinely missing schedule is a 404. An audit invariant, a transaction or an infrastructure failure
            // is not "not found": it reaches the host's handler (AUD-1.3 D19), as does an unknown or unconfirmed commit.
            return NotFound();
        }
    }

    /// <summary>Advisory preview of moving this Schedule to another ResourceContext (design gate §8.2).</summary>
    [HttpGet("{id}/ownership/move-preview")]
    public async Task<IActionResult> GetOwnershipMovePreview(
        string id, [FromQuery] string targetResourceContextId, CancellationToken cancellationToken)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        try
        {
            var preview = await _moveService.PreviewScheduleMoveAsync(
                new ScheduleId(id), new ResourceContextId(targetResourceContextId), cancellationToken);
            return Ok(ResourceMoveHttpMapper.ToDto(preview));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>Moves this Schedule to another ResourceContext. Re-validates identically inside the gated transaction (§8.2).</summary>
    [HttpPost("{id}/ownership/move")]
    public async Task<IActionResult> PostOwnershipMove(
        string id, [FromBody] Client.ResourceOwnershipMoveRequest body, CancellationToken cancellationToken)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        try
        {
            var preview = await _moveService.MoveScheduleAsync(
                new ScheduleId(id), new ResourceContextId(body.TargetResourceContextId), cancellationToken);
            return ResourceMoveHttpMapper.MapMoveResult(preview);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ResourceOwnershipBusyException ex) { return ResourceMoveHttpMapper.MapBusy(ex); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    private async Task<string?> TryGetWorkerRuntimeStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _schedulerStatus.GetStatusAsync(cancellationToken: cancellationToken);
            return snapshot.WorkerRuntime.Status ?? snapshot.Status;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static ScheduleDto ToDto(ScheduleDefinition d, DateTimeOffset asOfUtc, string? workerRuntimeStatus)
    {
        var clarity = ScheduleClarityProjector.Project(
            d.RecurrenceExpression,
            d.TimeZone,
            d.Enabled,
            d.IntegrationId?.Value,
            asOfUtc,
            workerRuntimeStatus: workerRuntimeStatus);

        return new ScheduleDto(
            d.Id.Value,
            d.Name,
            d.RecurrenceExpression,
            d.TimeZone,
            d.Enabled,
            (int)d.Target.JobType,
            d.Target.JobConfiguration,
            d.Version,
            d.IntegrationId?.Value,
            clarity.NextFireUtc,
            clarity.ScheduleTimeZoneId,
            clarity.HealthStatus,
            clarity.StatusReasons.Select(r => new ScheduleStatusReasonDto(r.Code, r.Message)).ToList(),
            d.ResourceContextId?.Value);
    }

    private static ScheduleDetailDto ToDetailDto(
        ScheduleWithExecutionState detail,
        string runtimePlaneId,
        DateTimeOffset asOfUtc,
        string? workerRuntimeStatus,
        ResourceContext? owningResourceContext)
    {
        var d = detail.Definition;
        var state = detail.ExecutionState;
        var claim = detail.InProgressClaim;
        var clarity = ScheduleClarityProjector.Project(
            d.RecurrenceExpression,
            d.TimeZone,
            d.Enabled,
            d.IntegrationId?.Value,
            asOfUtc,
            lastOutcome: state?.LastOutcome,
            isClaimStale: claim?.IsStale,
            workerRuntimeStatus: workerRuntimeStatus);

        return new ScheduleDetailDto(
            d.Id.Value,
            d.Name,
            d.RecurrenceExpression,
            d.TimeZone,
            d.Enabled,
            (int)d.Target.JobType,
            d.Target.JobConfiguration,
            d.Version,
            state?.LastAttemptedUtc,
            state?.LastOutcome,
            state?.LastOutcomeReason,
            state?.LastSuccessfulDispatchUtc,
            claim?.ClaimedByHostId,
            claim?.ClaimedAtUtc,
            claim?.IsStale,
            d.IntegrationId?.Value,
            runtimePlaneId,
            clarity.NextFireUtc,
            clarity.ScheduleTimeZoneId,
            clarity.HealthStatus,
            clarity.StatusReasons.Select(r => new ScheduleStatusReasonDto(r.Code, r.Message)).ToList(),
            d.ResourceContextId?.Value,
            owningResourceContext?.ToRef(),
            claim?.ResourceContextId);
    }

    private void TryAppendWarningHeader(string value)
    {
        if (HttpContext?.Response?.Headers is null)
            return;

        HttpContext.Response.Headers.Append("Warning", value);
    }
}

public sealed record ScheduleCapabilitiesDto(bool CanManage);

public sealed record ScheduleStatusReasonDto(string Code, string Message);

public sealed record ScheduleDto(
    string Id,
    string Name,
    string RecurrenceExpression,
    string TimeZone,
    bool Enabled,
    int TargetJobType,
    string? TargetJobConfiguration,
    int Version,
    string? IntegrationId = null,
    DateTimeOffset? NextFireUtc = null,
    string? ScheduleTimeZoneId = null,
    string? HealthStatus = null,
    IReadOnlyList<ScheduleStatusReasonDto>? StatusReasons = null,
    string? ResourceContextId = null);

/// <summary>
/// Schedule with execution state for operator visibility. Epic 28, Epic 36 (claim visibility).
/// <see cref="ClaimedByHostId"/> / <see cref="ClaimedAtUtc"/> / <see cref="IsClaimStale"/>: active in-progress claim only; not historical.
/// <see cref="ClaimResourceContextId"/>: CTX-2 historical snapshot from that same in-progress ScheduleRun — do not
/// confuse with <see cref="ResourceContextId"/> above, which is the Schedule's current ownership (CTX-1).
/// SWM-10: additive next-fire / readiness projection (compute-only).
/// </summary>
public sealed record ScheduleDetailDto(
    string Id,
    string Name,
    string RecurrenceExpression,
    string TimeZone,
    bool Enabled,
    int TargetJobType,
    string? TargetJobConfiguration,
    int Version,
    DateTimeOffset? LastAttemptedUtc,
    string? LastOutcome,
    string? LastOutcomeReason,
    DateTimeOffset? LastSuccessfulDispatchUtc,
    string? ClaimedByHostId = null,
    DateTimeOffset? ClaimedAtUtc = null,
    bool? IsClaimStale = null,
    string? IntegrationId = null,
    string? RuntimePlaneId = null,
    DateTimeOffset? NextFireUtc = null,
    string? ScheduleTimeZoneId = null,
    string? HealthStatus = null,
    IReadOnlyList<ScheduleStatusReasonDto>? StatusReasons = null,
    string? ResourceContextId = null,
    ResourceContextRef? ResourceContext = null,
    /// <summary>
    /// CTX-2: historical ResourceContext snapshot from the active in-progress ScheduleRun this claim
    /// belongs to. Distinct from <see cref="ResourceContextId"/> above, which is the Schedule's current
    /// ownership (CTX-1). Null whenever no in-progress claim exists.
    /// </summary>
    string? ClaimResourceContextId = null);

/// <param name="ResourceContextId">
/// Optional owning ResourceContext. For an Integration-attached schedule it must equal the
/// Integration's ResourceContext; omitted, the schedule inherits it. A standalone schedule defaults
/// to the reserved Shared context.
/// </param>
public sealed record CreateScheduleRequestBody(
    string Name,
    string RecurrenceExpression,
    string? TimeZone = "UTC",
    bool Enabled = true,
    int? TargetJobType = 0,
    string? TargetJobConfiguration = null,
    string? ExecutionPolicyRef = null,
    string? RuntimePlaneId = "local",
    string? IntegrationId = null,
    string? ResourceContextId = null);

/// <summary>
/// Epic 38: Target kind and configuration are immutable after creation.
/// If TargetJobType or TargetJobConfiguration is provided, the API returns 400.
/// </summary>
public sealed record UpdateScheduleRequestBody(
    string? Name = null,
    string? RecurrenceExpression = null,
    string? TimeZone = null,
    bool? Enabled = null,
    int? TargetJobType = null,
    string? TargetJobConfiguration = null);
