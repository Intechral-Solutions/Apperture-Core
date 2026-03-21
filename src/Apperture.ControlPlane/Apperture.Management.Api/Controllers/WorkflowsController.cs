using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Management.Api.Models;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
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
    private readonly IAuditService? _audit;
    private readonly IPrincipalContext _principal;

    public WorkflowsController(
        IWorkflowStartAuthorizationService workflowStartAuth,
        IWorkflowStartDispatchService dispatch,
        IPrincipalContext principal,
        IAuditService? audit = null)
    {
        _workflowStartAuth = workflowStartAuth;
        _dispatch = dispatch;
        _principal = principal;
        _audit = audit;
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
        {
            await TryAppendStartBlockedAuditAsync(
                LifecycleExecutionAuditActionNames.WorkflowStartBlocked,
                $"workflowId={workflowId};reason=definition_not_executable",
                body?.CorrelationId,
                cancellationToken).ConfigureAwait(false);
            return NotFound(new { error = $"Workflow definition {workflowId} not found." });
        }

        return Accepted(new
        {
            runId = dispatchResult.RunId.Value,
            workflowInstanceId = dispatchResult.WorkflowInstanceId.Value,
            workflowDefinitionId = dispatchResult.WorkflowDefinitionId.Value,
            status = dispatchResult.Status
        });
    }

    private async Task TryAppendStartBlockedAuditAsync(
        string action,
        string details,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (_audit is null)
            return;

        var actor = await _principal.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return;

        try
        {
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    actor.Id,
                    action,
                    Resource: null,
                    Outcome: "Blocked",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(correlationId),
                    details),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort audit
        }
    }

    // Future: POST workflows/{instanceId}/signal — must call OrchestrationSafeValidator.ValidateSignalPayload(payload) at entry before persisting to orchestration state/history.
}
