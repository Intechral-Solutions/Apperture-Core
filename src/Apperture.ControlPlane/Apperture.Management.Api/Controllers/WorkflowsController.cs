using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.Management.Api.Models;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize]
public sealed class WorkflowsController : ControllerBase
{
    private const string DirectStartDeprecationHeader = "299 - \"Direct workflow starts are deprecated; use integration procedure launch APIs where available.\"";
    private readonly IWorkflowStartAuthorizationService _workflowStartAuth;
    private readonly IAuditedExecutionStartService _starts;

    public WorkflowsController(
        IWorkflowStartAuthorizationService workflowStartAuth,
        IAuditedExecutionStartService starts)
    {
        _workflowStartAuth = workflowStartAuth;
        _starts = starts;
    }

    [HttpPost("{workflowId}/start")]
    public async Task<ActionResult> Start([FromRoute] string workflowId, [FromBody] StartWorkflowRequestBody? body, CancellationToken cancellationToken)
    {
        TryAppendWarningHeader(DirectStartDeprecationHeader);

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
        if (string.IsNullOrWhiteSpace(body?.IntegrationId))
        {
            return BadRequest(new { error = "IntegrationId is required. Manual workflow execution must target an integration." });
        }

        var integrationId = body.IntegrationId;

        var request = new StartWorkflowRequest(
            new WorkflowDefinitionId(workflowId),
            parameters,
            body?.IdempotencyKey,
            body?.CorrelationId,
            RetryOfRunId: null,
            IntegrationId: integrationId,
            ProcedureId: body?.ProcedureId,
            ExecutionOrigin: RunExecutionOriginNames.Manual);

        // The start, its request row (which names the requester) and its audit row commit together; a definition that
        // is not executable creates nothing and is recorded as a refused start by the same actor.
        WorkflowStartDispatchResult? dispatchResult;
        try
        {
            dispatchResult = await _starts.StartWorkflowAsync(request, ExecutionLaunchChannel.Direct, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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

    private void TryAppendWarningHeader(string value)
    {
        if (HttpContext?.Response?.Headers is null)
            return;
        HttpContext.Response.Headers.Append("Warning", value);
    }

    // Future: POST workflows/{instanceId}/signal — must call OrchestrationSafeValidator.ValidateSignalPayload(payload) at entry before persisting to orchestration state/history.
}
