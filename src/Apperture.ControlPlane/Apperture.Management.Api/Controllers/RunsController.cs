using Apperture.ControlPlane.Security;
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
    private readonly IWorkflowCancelService _cancel;
    private readonly IWorkflowRetryService _retry;
    private readonly IWorkflowExternalControlService _externalControl;

    public RunsController(IRunQueryService runs, IWorkflowCancelService cancel, IWorkflowRetryService retry, IWorkflowExternalControlService externalControl)
    {
        _runs = runs;
        _cancel = cancel;
        _retry = retry;
        _externalControl = externalControl;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RunSummaryDto>>> GetRuns(CancellationToken cancellationToken)
    {
        try
        {
            var list = await _runs.ListRunsAsync(cancellationToken);
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
