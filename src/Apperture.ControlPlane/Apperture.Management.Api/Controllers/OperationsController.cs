using Apperture.Core.Domain.Operations;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Management.Api.Models;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("operations")]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class OperationsController : ControllerBase
{
    private readonly IOperationStartAuthorizationService _startAuth;
    private readonly IOperationStartDispatchService _dispatch;

    public OperationsController(
        IOperationStartAuthorizationService startAuth,
        IOperationStartDispatchService dispatch)
    {
        _startAuth = startAuth;
        _dispatch = dispatch;
    }

    [HttpPost("{operationId}/start")]
    public async Task<ActionResult> Start([FromRoute] string operationId, [FromBody] StartOperationRequestBody? body, CancellationToken cancellationToken)
    {
        try
        {
            await _startAuth.EnsureCanStartOperationAsync(new OperationId(operationId), cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        var parameters = body?.Parameters;
        OrchestrationSafeValidator.ValidateParameters(parameters);

        var request = new StartOperationRequest(
            operationId,
            parameters,
            body?.IdempotencyKey,
            body?.CorrelationId);

        var dispatchResult = await _dispatch.StartAsync(request, cancellationToken);
        if (dispatchResult is null)
            return NotFound(new { error = $"Operation definition {operationId} not found." });

        return Accepted(new
        {
            runId = dispatchResult.RunId.Value,
            operationInstanceId = dispatchResult.OperationInstanceId.Value,
            operationDefinitionId = dispatchResult.OperationDefinitionId.Value,
            status = dispatchResult.Status
        });
    }
}

