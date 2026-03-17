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

    public RunsController(IRunQueryService runs, IWorkflowCancelService cancel)
    {
        _runs = runs;
        _cancel = cancel;
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
}
