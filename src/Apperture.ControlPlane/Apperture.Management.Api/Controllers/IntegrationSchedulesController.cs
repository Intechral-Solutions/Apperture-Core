using Apperture.ControlPlane.Security;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Integration-owned schedules. Epic 50 / SWM-3 timezone defaults / SWM-10 next-fire clarity.
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
    private readonly ISchedulerStatusQueryService _schedulerStatus;

    public IntegrationSchedulesController(
        IIntegrationManagementService integrationManagement,
        IScheduleRepository scheduleRepository,
        IScheduleManagementService scheduleManagement,
        IScheduleGovernanceService scheduleGovernance,
        IScheduleValidationService scheduleValidation,
        IScheduleCreateTimeZoneDefaults createTimeZoneDefaults,
        ISchedulerStatusQueryService schedulerStatus)
    {
        _integrationManagement = integrationManagement;
        _scheduleRepository = scheduleRepository;
        _scheduleManagement = scheduleManagement;
        _scheduleGovernance = scheduleGovernance;
        _scheduleValidation = scheduleValidation;
        _createTimeZoneDefaults = createTimeZoneDefaults;
        _schedulerStatus = schedulerStatus;
    }

    /// <summary>
    /// Lists schedules owned by this Integration (includes SWM-10 next-fire / readiness).
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
            var asOf = DateTimeOffset.UtcNow;
            var workerStatus = await TryGetWorkerRuntimeStatusAsync(cancellationToken);
            return Ok(definitions.Select(d => ToDto(d, asOf, workerStatus)).ToList());
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
                id)
            {
                // Omitted means inherit the Integration's ResourceContext; the management service
                // applies that rule and rejects an explicit context that disagrees with it.
                ResourceContextId = string.IsNullOrWhiteSpace(body.ResourceContextId)
                    ? null
                    : new ResourceContextId(body.ResourceContextId)
            };

            var created = await _scheduleManagement.CreateScheduleAsync(definition, cancellationToken);
            var planeId = new RuntimePlaneId(body.RuntimePlaneId ?? "local");
            await _scheduleManagement.AssignToRuntimePlaneAsync(created.Id, planeId, null, cancellationToken);

            return CreatedAtAction(nameof(List), new { integrationId }, ToDto(created, DateTimeOffset.UtcNow, workerRuntimeStatus: null));
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
}

/// <param name="ResourceContextId">
/// Optional. Omitted, the schedule inherits the Integration's ResourceContext. Supplied, it must equal
/// the Integration's ResourceContext or the request is rejected.
/// </param>
public sealed record CreateIntegrationScheduleRequestBody(
    string Name,
    string RecurrenceExpression,
    string? TimeZone = "UTC",
    bool Enabled = true,
    int? TargetJobType = 0,
    string? TargetJobConfiguration = null,
    string? ExecutionPolicyRef = null,
    string? RuntimePlaneId = "local",
    string? ResourceContextId = null);
