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

    public SchedulesController(IScheduleGovernanceService scheduleGovernance, IScheduleManagementService scheduleManagement)
    {
        _scheduleGovernance = scheduleGovernance;
        _scheduleManagement = scheduleManagement;
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
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScheduleDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        var def = await _scheduleManagement.GetDefinitionAsync(new ScheduleId(id), cancellationToken);
        if (def is null)
            return NotFound();
        return Ok(ToDto(def));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScheduleDto>> Create([FromBody] CreateScheduleRequestBody body, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
        var id = Guid.NewGuid().ToString("N")[..24];
        var target = new ScheduledTarget(ScheduledJobType.WorkflowExecution, body.TargetJobConfiguration);
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Update(string id, [FromBody] UpdateScheduleRequestBody body, CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
            return Forbid();
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
        return Ok();
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

public sealed record CreateScheduleRequestBody(
    string Name,
    string RecurrenceExpression,
    string? TimeZone = "UTC",
    bool Enabled = true,
    string? TargetJobConfiguration = null,
    string? ExecutionPolicyRef = null,
    string? RuntimePlaneId = "local");

public sealed record UpdateScheduleRequestBody(
    string? Name = null,
    string? RecurrenceExpression = null,
    string? TimeZone = null,
    bool? Enabled = null);

