using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Management.Api.Models;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("workflows")]
[Authorize]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IWorkflowStartAuthorizationService _workflowStartAuth;
    private readonly IWorkflowStartDispatchService _dispatch;

    public WorkflowsController(IWorkflowStartAuthorizationService workflowStartAuth, IWorkflowStartDispatchService dispatch)
    {
        _workflowStartAuth = workflowStartAuth;
        _dispatch = dispatch;
    }

    [HttpPost("{workflowId}/start")]
    public async Task<ActionResult> Start([FromRoute] string workflowId, [FromBody] StartWorkflowRequestBody? body, CancellationToken cancellationToken)
    {
        try
        {
            await _workflowStartAuth.EnsureCanStartWorkflowAsync(new WorkflowDefinitionId(workflowId), cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        var parameters = body?.Parameters;
        OrchestrationSafeValidator.ValidateParameters(parameters);

        var request = new StartWorkflowRequest(
            new WorkflowDefinitionId(workflowId),
            parameters,
            body?.IdempotencyKey,
            body?.CorrelationId);

        var dispatchResult = await _dispatch.StartAsync(request, cancellationToken);
        if (dispatchResult is null)
            return NotFound(new { error = $"Workflow definition {workflowId} not found." });

        return Accepted(new
        {
            runId = dispatchResult.RunId.Value,
            workflowInstanceId = dispatchResult.WorkflowInstanceId.Value,
            workflowDefinitionId = dispatchResult.WorkflowDefinitionId.Value,
            status = dispatchResult.Status
        });
    }

    // Future: POST workflows/{instanceId}/signal — must call OrchestrationSafeValidator.ValidateSignalPayload(payload) at entry before persisting to orchestration state/history.
}
