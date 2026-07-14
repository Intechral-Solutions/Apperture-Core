using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
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

    public SchedulesController(
        IScheduleGovernanceService scheduleGovernance,
        IScheduleManagementService scheduleManagement,
        IScheduleValidationService scheduleValidation,
        IScheduleCreateTimeZoneDefaults createTimeZoneDefaults)
    {
        _scheduleGovernance = scheduleGovernance;
        _scheduleManagement = scheduleManagement;
        _scheduleValidation = scheduleValidation;
        _createTimeZoneDefaults = createTimeZoneDefaults;
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

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ScheduleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ScheduleDto>>> List(CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        var definitions = await _scheduleManagement.ListDefinitionsAsync(cancellationToken);
        if (definitions.Any(d => d.IntegrationId is null))
            TryAppendWarningHeader(LegacyScheduleWarningHeader);
        return Ok(definitions.Select(ToDto).ToList());
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
        return Ok(ToDetailDto(detail));
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
            integrationId);
        await _scheduleManagement.CreateScheduleAsync(definition, cancellationToken);
        var planeId = new RuntimePlaneId(body.RuntimePlaneId ?? "local");
        await _scheduleManagement.AssignToRuntimePlaneAsync(definition.Id, planeId, null, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, ToDto(definition));
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
        var def = await _scheduleManagement.GetDefinitionAsync(new ScheduleId(id), cancellationToken);
        if (def is null)
            return NotFound();
        var updated = def with
        {
            Name = body.Name ?? def.Name,
            RecurrenceExpression = body.RecurrenceExpression ?? def.RecurrenceExpression,
            TimeZone = body.TimeZone ?? def.TimeZone,
            Enabled = body.Enabled ?? def.Enabled
        };
        await _scheduleManagement.UpdateScheduleAsync(updated, cancellationToken);
        return Ok(ToDto(updated));
    }

    [HttpPost("{id}/enable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Enable(string id, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        try
        {
            await _scheduleManagement.EnableAsync(new ScheduleId(id), cancellationToken);
            return Ok();
        }
        catch (InvalidOperationException)
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
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private static ScheduleDto ToDto(ScheduleDefinition d) => new(
        d.Id.Value,
        d.Name,
        d.RecurrenceExpression,
        d.TimeZone,
        d.Enabled,
        (int)d.Target.JobType,
        d.Target.JobConfiguration,
        d.Version,
        d.IntegrationId?.Value);

    private static ScheduleDetailDto ToDetailDto(ScheduleWithExecutionState detail)
    {
        var d = detail.Definition;
        var state = detail.ExecutionState;
        var claim = detail.InProgressClaim;
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
            d.IntegrationId?.Value);
    }

    private void TryAppendWarningHeader(string value)
    {
        if (HttpContext?.Response?.Headers is null)
            return;

        HttpContext.Response.Headers.Append("Warning", value);
    }
}

public sealed record ScheduleCapabilitiesDto(bool CanManage);

public sealed record ScheduleDto(
    string Id,
    string Name,
    string RecurrenceExpression,
    string TimeZone,
    bool Enabled,
    int TargetJobType,
    string? TargetJobConfiguration,
    int Version,
    string? IntegrationId = null);

/// <summary>
/// Schedule with execution state for operator visibility. Epic 28, Epic 36 (claim visibility).
/// <see cref="ClaimedByHostId"/> / <see cref="ClaimedAtUtc"/> / <see cref="IsClaimStale"/>: active in-progress claim only; not historical.
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
    string? IntegrationId = null);

public sealed record CreateScheduleRequestBody(
    string Name,
    string RecurrenceExpression,
    string? TimeZone = "UTC",
    bool Enabled = true,
    int? TargetJobType = 0,
    string? TargetJobConfiguration = null,
    string? ExecutionPolicyRef = null,
    string? RuntimePlaneId = "local",
    string? IntegrationId = null);

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

