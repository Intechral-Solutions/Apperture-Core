using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/schedules")]
[Authorize]
public sealed class SchedulesController : ControllerBase
{
    private readonly IScheduleGovernanceService _scheduleGovernance;
    private readonly IScheduleManagementService _scheduleManagement;
    private readonly IScheduleValidationService _scheduleValidation;

    public SchedulesController(IScheduleGovernanceService scheduleGovernance, IScheduleManagementService scheduleManagement, IScheduleValidationService scheduleValidation)
    {
        _scheduleGovernance = scheduleGovernance;
        _scheduleManagement = scheduleManagement;
        _scheduleValidation = scheduleValidation;
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
        return Ok(ToDetailDto(detail));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScheduleDto>> Create([FromBody] CreateScheduleRequestBody body, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        var validation = _scheduleValidation.ValidateCreate(
            body.Name,
            body.RecurrenceExpression,
            body.TimeZone,
            body.Enabled,
            body.TargetJobType,
            body.TargetJobConfiguration);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors });
        var jobType = (ScheduledJobType)(body.TargetJobType ?? 0);
        var id = Guid.NewGuid().ToString("N")[..24];
        var target = new ScheduledTarget(jobType, body.TargetJobConfiguration!);
        var definition = new ScheduleDefinition(
            new ScheduleId(id),
            body.Name,
            body.RecurrenceExpression,
            body.TimeZone ?? "UTC",
            body.Enabled,
            target,
            body.ExecutionPolicyRef,
            Version: 1);
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
        d.Version);

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
            claim?.IsStale);
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
    int Version);

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
    bool? IsClaimStale = null);

public sealed record CreateScheduleRequestBody(
    string Name,
    string RecurrenceExpression,
    string? TimeZone = "UTC",
    bool Enabled = true,
    int? TargetJobType = 0,
    string? TargetJobConfiguration = null,
    string? ExecutionPolicyRef = null,
    string? RuntimePlaneId = "local");

public sealed record UpdateScheduleRequestBody(
    string? Name = null,
    string? RecurrenceExpression = null,
    string? TimeZone = null,
    bool? Enabled = null);

