using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ControlFacade;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("runs")]
[Authorize]
public sealed class RunsController : ControllerBase
{
    private readonly IRunQueryService _runs;
    private readonly IExecutionCancelService _cancel;
    private readonly IWorkflowRetryService _retry;
    private readonly IWorkflowExternalControlService _externalControl;
    private readonly IRunActionAvailabilityQueryService _runActions;
    private readonly IRunTimelineQueryService _timeline;

    public RunsController(
        IRunQueryService runs,
        IExecutionCancelService cancel,
        IWorkflowRetryService retry,
        IWorkflowExternalControlService externalControl,
        IRunActionAvailabilityQueryService runActions,
        IRunTimelineQueryService timeline)
    {
        _runs = runs;
        _cancel = cancel;
        _retry = retry;
        _externalControl = externalControl;
        _runActions = runActions;
        _timeline = timeline;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RunSummaryDto>>> GetRuns(
        [FromQuery] string? executionKind,
        [FromQuery] string? operationDefinitionId,
        [FromQuery] string? status,
        [FromQuery] DateTimeOffset? createdFrom,
        [FromQuery] DateTimeOffset? createdTo,
        [FromQuery] string? moduleId,
        [FromQuery] string? correlationKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var filter = new RunQueryFilter(
                ExecutionKind: executionKind,
                OperationDefinitionId: operationDefinitionId,
                Status: status,
                CreatedFrom: createdFrom,
                CreatedTo: createdTo,
                ModuleId: moduleId,
                CorrelationKey: correlationKey);
            var list = await _runs.ListRunsAsync(filter, cancellationToken);
            return Ok(list);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RunDetailDto>> GetRun(string id, CancellationToken cancellationToken)
    {
        try
        {
            var run = await _runs.GetRunAsync(new OperationRunId(id), cancellationToken);
            if (run is null)
                return NotFound();
            return Ok(run);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}/actions")]
    public async Task<ActionResult<IReadOnlyList<ControlActionAvailabilityDto>>> GetRunActions(string id, CancellationToken cancellationToken)
    {
        try
        {
            var actions = await _runActions.GetRunActionsAsync(new OperationRunId(id), cancellationToken);
            if (actions is null)
                return NotFound();
            return Ok(actions);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}/timeline")]
    public async Task<ActionResult<RunTimelineDto>> GetRunTimeline(string id, CancellationToken cancellationToken)
    {
        try
        {
            var timeline = await _timeline.GetRunTimelineAsync(new OperationRunId(id), cancellationToken);
            if (timeline is null)
                return NotFound();
            return Ok(timeline);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult> CancelRun(string id, CancellationToken cancellationToken)
    {
        try
        {
            var ok = await _cancel.RequestCancelAsync(new OperationRunId(id), cancellationToken);
            if (!ok)
                return NotFound();
            return Accepted(new { runId = id, status = "cancel_requested" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("{id}/retry")]
    public async Task<ActionResult> RetryRun(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _retry.RequestRetryAsync(new OperationRunId(id), cancellationToken);
            if (result is null)
                return NotFound();

            return Accepted(new
            {
                originalRunId = result.OriginalRunId.Value,
                runId = result.RunId.Value,
                workflowInstanceId = result.WorkflowInstanceId.Value,
                workflowDefinitionId = result.WorkflowDefinitionId.Value,
                status = result.Status,
                retryOfRunId = result.OriginalRunId.Value
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/park")]
    public async Task<ActionResult> ParkRun(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _externalControl.RequestParkAsync(new OperationRunId(id), cancellationToken);
            if (result is null)
                return NotFound();

            return Accepted(new
            {
                runId = result.RunId.Value,
                workflowInstanceId = result.WorkflowInstanceId.Value,
                workflowDefinitionId = result.WorkflowDefinitionId.Value,
                controlKind = result.ControlKind,
                status = result.Status,
                runStatus = result.RunStatus,
                currentStepName = result.CurrentStepName
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/resume")]
    public async Task<ActionResult> ResumeRun(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _externalControl.RequestResumeAsync(new OperationRunId(id), cancellationToken);
            if (result is null)
                return NotFound();

            return Accepted(new
            {
                runId = result.RunId.Value,
                workflowInstanceId = result.WorkflowInstanceId.Value,
                workflowDefinitionId = result.WorkflowDefinitionId.Value,
                controlKind = result.ControlKind,
                status = result.Status,
                runStatus = result.RunStatus,
                currentStepName = result.CurrentStepName
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }
}
