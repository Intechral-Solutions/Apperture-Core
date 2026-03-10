using Apperture.Core.Application;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("workflows")]
public sealed class WorkflowsController : ControllerBase
{
    [HttpPost("{workflowId}/start")]
    public ActionResult<object> Start(string workflowId, [FromBody] StartWorkflowRequest? request)
    {
        if (request is not null)
            OrchestrationSafeValidator.ValidateParameters(request.Parameters);

        return StatusCode(501, new { message = "Not implemented" });
    }
}
