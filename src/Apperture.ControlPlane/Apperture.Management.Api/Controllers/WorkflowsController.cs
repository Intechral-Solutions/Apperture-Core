using Apperture.Core.Application;
using Apperture.Management.Api.Models;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("workflows")]
public sealed class WorkflowsController : ControllerBase
{
    [HttpPost("{workflowId}/start")]
    public ActionResult Start([FromRoute] string workflowId, [FromBody] StartWorkflowRequestBody? body)
    {
        var parameters = body?.Parameters;
        OrchestrationSafeValidator.ValidateParameters(parameters);

        var request = new StartWorkflowRequest(
            new WorkflowDefinitionId(workflowId),
            parameters,
            body?.IdempotencyKey,
            body?.CorrelationId);

        _ = request; // Not yet passed to runtime (501); when implemented, call IWorkflowRuntime.StartAsync(request).
        return StatusCode(501, new { message = "Not implemented" });
    }

    // Future: POST workflows/{instanceId}/signal — must call OrchestrationSafeValidator.ValidateSignalPayload(payload) at entry before persisting to orchestration state/history.
}
