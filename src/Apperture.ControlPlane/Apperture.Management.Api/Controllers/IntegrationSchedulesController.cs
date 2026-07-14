using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Integration-owned schedules. Epic 50 / SWM-3 timezone defaults.
/// </summary>
[ApiController]
[Route("api/integrations/{integrationId}/schedules")]
[Authorize]
public sealed class IntegrationSchedulesController : ControllerBase
{
    private readonly IIntegrationManagementService _integrationManagement;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IScheduleManagementService _scheduleManagement;
    private readonly IScheduleGovernanceService _scheduleGovernance;
    private readonly IScheduleValidationService _scheduleValidation;
    private readonly IScheduleCreateTimeZoneDefaults _createTimeZoneDefaults;

    public IntegrationSchedulesController(
        IIntegrationManagementService integrationManagement,
        IScheduleRepository scheduleRepository,
        IScheduleManagementService scheduleManagement,
        IScheduleGovernanceService scheduleGovernance,
        IScheduleValidationService scheduleValidation,
        IScheduleCreateTimeZoneDefaults createTimeZoneDefaults)
    {
        _integrationManagement = integrationManagement;
        _scheduleRepository = scheduleRepository;
        _scheduleManagement = scheduleManagement;
        _scheduleGovernance = scheduleGovernance;
        _scheduleValidation = scheduleValidation;
        _createTimeZoneDefaults = createTimeZoneDefaults;
    }

    /// <summary>
    /// Lists schedules owned by this Integration.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScheduleDto>>> List(string integrationId, CancellationToken cancellationToken)
    {
        try
        {
            var id = new IntegrationId(integrationId);
            var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
            if (integration is null)
                return NotFound();

            if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
                return Forbid();

            var definitions = await _scheduleRepository.GetByIntegrationIdAsync(id, cancellationToken);
            return Ok(definitions.Select(ToDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Creates a schedule for this Integration. Assigns to default runtime plane.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ScheduleDto>> Create(string integrationId, [FromBody] CreateIntegrationScheduleRequestBody body, CancellationToken cancellationToken)
    {
        try
        {
            var id = new IntegrationId(integrationId);
            var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
            if (integration is null)
                return NotFound();

            if (!await _scheduleGovernance.CanManageAsync(cancellationToken))
                return Forbid();

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
            var scheduleId = Guid.NewGuid().ToString("N")[..24];
            var target = new ScheduledTarget(jobType, body.TargetJobConfiguration!);
            var definition = new ScheduleDefinition(
                new ScheduleId(scheduleId),
                body.Name,
                body.RecurrenceExpression,
                timeZone,
                body.Enabled,
                target,
                body.ExecutionPolicyRef,
                Version: 1,
                id);

            await _scheduleManagement.CreateScheduleAsync(definition, cancellationToken);
            var planeId = new RuntimePlaneId(body.RuntimePlaneId ?? "local");
            await _scheduleManagement.AssignToRuntimePlaneAsync(definition.Id, planeId, null, cancellationToken);

            return CreatedAtAction(nameof(List), new { integrationId }, ToDto(definition));
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
}

public sealed record CreateIntegrationScheduleRequestBody(
    string Name,
    string RecurrenceExpression,
    string? TimeZone = "UTC",
    bool Enabled = true,
    int? TargetJobType = 0,
    string? TargetJobConfiguration = null,
    string? ExecutionPolicyRef = null,
    string? RuntimePlaneId = "local");
