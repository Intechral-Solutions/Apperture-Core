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
    private readonly IOperationDefinitionQueryService _definitions;
    private readonly IOperationStartAuthorizationService _startAuth;
    private readonly IOperationInputValidationService _inputValidation;
    private readonly IOperationStartDispatchService _dispatch;

    public OperationsController(
        IOperationDefinitionQueryService definitions,
        IOperationStartAuthorizationService startAuth,
        IOperationInputValidationService inputValidation,
        IOperationStartDispatchService dispatch)
    {
        _definitions = definitions;
        _startAuth = startAuth;
        _inputValidation = inputValidation;
        _dispatch = dispatch;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OperationDefinitionDto>>> GetOperations(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _definitions.ListAsync(cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{operationDefinitionId}")]
    public async Task<ActionResult<OperationDefinitionDto>> GetOperation([FromRoute] string operationDefinitionId, CancellationToken cancellationToken)
    {
        try
        {
            var definition = await _definitions.GetAsync(new OperationId(operationDefinitionId), cancellationToken);
            if (definition is null)
                return NotFound();
            return Ok(definition);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("{operationDefinitionId}/start")]
    public async Task<ActionResult> Start([FromRoute] string operationDefinitionId, [FromBody] StartOperationRequestBody? body, CancellationToken cancellationToken)
    {
        try
        {
            await _startAuth.EnsureCanStartOperationAsync(new OperationId(operationDefinitionId), cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        var parameters = body?.Parameters;
        try
        {
            OrchestrationSafeValidator.ValidateParameters(parameters);
            await _inputValidation.ValidateAsync(new OperationId(operationDefinitionId), parameters, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var request = new StartOperationRequest(
            operationDefinitionId,
            parameters,
            body?.IdempotencyKey,
            body?.CorrelationId);

        var dispatchResult = await _dispatch.StartAsync(request, cancellationToken);
        if (dispatchResult is null)
            return NotFound(new { error = $"Operation definition {operationDefinitionId} not found." });

        return Accepted(new
        {
            runId = dispatchResult.RunId.Value,
            operationInstanceId = dispatchResult.OperationInstanceId.Value,
            operationDefinitionId = dispatchResult.OperationDefinitionId.Value,
            status = dispatchResult.Status
        });
    }
}

