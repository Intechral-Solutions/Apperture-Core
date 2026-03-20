using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Runtime;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Security.Trust;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class ControlActionAvailabilityProjection : IControlActionAvailabilityProjection
{
    private readonly IPrincipalContext _principal;
    private readonly IAuthorizationEvaluationService _authorization;
    private readonly IEnvironmentContext _environment;

    private readonly IOperationRunCancelAuthorizationService _operationCancelAuthz;
    private readonly IWorkflowRunCancelAuthorizationService _workflowCancelAuthz;
    private readonly IWorkflowRunRetryAuthorizationService _workflowRetryAuthz;
    private readonly IWorkflowExternalControlAuthorizationService _workflowExternalControlAuthz;

    private readonly IWorkflowStartAuthorizationService _workflowStartAuthz;

    public ControlActionAvailabilityProjection(
        IPrincipalContext principal,
        IAuthorizationEvaluationService authorization,
        IEnvironmentContext environment,
        IOperationRunCancelAuthorizationService operationCancelAuthz,
        IWorkflowRunCancelAuthorizationService workflowCancelAuthz,
        IWorkflowRunRetryAuthorizationService workflowRetryAuthz,
        IWorkflowExternalControlAuthorizationService workflowExternalControlAuthz,
        IWorkflowStartAuthorizationService workflowStartAuthz)
    {
        _principal = principal;
        _authorization = authorization;
        _environment = environment;
        _operationCancelAuthz = operationCancelAuthz;
        _workflowCancelAuthz = workflowCancelAuthz;
        _workflowRetryAuthz = workflowRetryAuthz;
        _workflowExternalControlAuthz = workflowExternalControlAuthz;
        _workflowStartAuthz = workflowStartAuthz;
    }

    public async ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectExecutionRunActionsAsync(
        RunSummaryDto run,
        bool hasQueuedPark,
        bool hasQueuedResume,
        CancellationToken cancellationToken = default)
    {
        var actions = new List<ControlActionAvailabilityDto>(4);

        var isWorkflow = string.Equals(run.ExecutionKind, "workflow", StringComparison.OrdinalIgnoreCase);
        var status = run.Status ?? "";

        // Cancel is for both execution kinds, but only for non-terminal states.
        if (IsTerminalRunStatus(status))
        {
            actions.Add(new ControlActionAvailabilityDto(ControlFacade.ActionKindCancel, ControlFacade.ActionAvailabilityNotEligible, ControlFacade.ReasonStateNotEligible));
        }
        else
        {
            actions.Add(await ProjectCancelAvailabilityAsync(run, isWorkflow, cancellationToken).ConfigureAwait(false));
        }

        if (isWorkflow)
        {
            actions.Add(await ProjectRetryAvailabilityAsync(run, cancellationToken).ConfigureAwait(false));
            actions.Add(await ProjectParkAvailabilityAsync(run, hasQueuedPark, cancellationToken).ConfigureAwait(false));
            actions.Add(await ProjectResumeAvailabilityAsync(run, hasQueuedResume, cancellationToken).ConfigureAwait(false));
        }

        return actions;
    }

    public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectOperationDefinitionActionsAsync(
        OperationDefinitionDto operation,
        CancellationToken cancellationToken = default)
    {
        var actions = operation.CanStart
            ? new List<ControlActionAvailabilityDto> { new(ControlFacade.ActionKindOperationStart, ControlFacade.ActionAvailabilityAvailable, null) }
            : new List<ControlActionAvailabilityDto> { new(ControlFacade.ActionKindOperationStart, ControlFacade.ActionAvailabilityUnavailable, ControlFacade.ReasonPermissionMissing) };

        return ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(actions);
    }

    public async ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectWorkflowDefinitionActionsAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _workflowStartAuthz.EnsureCanStartWorkflowAsync(workflow.Id, cancellationToken).ConfigureAwait(false);
            return new List<ControlActionAvailabilityDto> { new(ControlFacade.ActionKindWorkflowStart, ControlFacade.ActionAvailabilityAvailable, null) };
        }
        catch (UnauthorizedAccessException)
        {
            return new List<ControlActionAvailabilityDto> { new(ControlFacade.ActionKindWorkflowStart, ControlFacade.ActionAvailabilityUnavailable, ControlFacade.ReasonPermissionMissing) };
        }
    }

    public async ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectRuntimeInstanceActionsAsync(
        RuntimeIdentity runtime,
        CancellationToken cancellationToken = default)
    {
        var actor = await _principal.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return Array.Empty<ControlActionAvailabilityDto>();

        var envId = _environment.EnvironmentId;
        var runtimeScopeKey = ScopeKeyConstants.RuntimeInstance(envId, runtime.Id);
        var permission = new PermissionDescriptor(
            ActionConstants.RuntimeControlShutdownRequest,
            new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, runtimeScopeKey));

        var hasCapability = runtime.DeclaredScopes.Contains(RuntimeCapabilityCatalog.ControlShutdown, StringComparer.Ordinal);
        var allowed = hasCapability
            && await _authorization.EvaluateAsync(
                new AuthorizationEvaluationContext(actor, permission, permission.Resource),
                cancellationToken).ConfigureAwait(false);

        if (allowed)
            return new List<ControlActionAvailabilityDto> { new(ControlFacade.ActionKindRuntimeShutdownRequest, ControlFacade.ActionAvailabilityAvailable, null) };

        var reason = !hasCapability ? ControlFacade.ReasonCapabilityMissing : ControlFacade.ReasonPermissionMissing;
        return new List<ControlActionAvailabilityDto> { new(ControlFacade.ActionKindRuntimeShutdownRequest, ControlFacade.ActionAvailabilityUnavailable, reason) };
    }

    private async ValueTask<ControlActionAvailabilityDto> ProjectCancelAvailabilityAsync(
        RunSummaryDto run,
        bool isWorkflow,
        CancellationToken cancellationToken)
    {
        try
        {
            if (isWorkflow)
                await _workflowCancelAuthz.EnsureCanCancelRunAsync(new WorkflowDefinitionId(run.ExecutionDefinitionId), cancellationToken).ConfigureAwait(false);
            else
                await _operationCancelAuthz.EnsureCanCancelRunAsync(new OperationId(run.ExecutionDefinitionId), cancellationToken).ConfigureAwait(false);

            return new ControlActionAvailabilityDto(ControlFacade.ActionKindCancel, ControlFacade.ActionAvailabilityAvailable, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindCancel, ControlFacade.ActionAvailabilityUnavailable, ControlFacade.ReasonPermissionMissing);
        }
    }

    private async ValueTask<ControlActionAvailabilityDto> ProjectRetryAvailabilityAsync(
        RunSummaryDto run,
        CancellationToken cancellationToken)
    {
        // Retry only makes sense for terminal failures (failed/cancelled) in this facade.
        if (!IsRetryableStatus(run.Status))
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindRetry, ControlFacade.ActionAvailabilityNotEligible, ControlFacade.ReasonStateNotEligible);

        try
        {
            await _workflowRetryAuthz.EnsureCanRetryRunAsync(new WorkflowDefinitionId(run.ExecutionDefinitionId), cancellationToken).ConfigureAwait(false);
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindRetry, ControlFacade.ActionAvailabilityAvailable, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindRetry, ControlFacade.ActionAvailabilityUnavailable, ControlFacade.ReasonPermissionMissing);
        }
    }

    private ValueTask<ControlActionAvailabilityDto> ProjectParkAvailabilityAsync(
        RunSummaryDto run,
        bool hasQueuedPark,
        CancellationToken cancellationToken)
    {
        // Park only valid when workflow is not terminal and not already parked.
        if (hasQueuedPark)
            return ValueTask.FromResult(new ControlActionAvailabilityDto(ControlFacade.ActionKindPark, ControlFacade.ActionAvailabilityAlreadyQueued, ControlFacade.ReasonAlreadyQueued));

        if (string.Equals(run.Status, "parked", StringComparison.OrdinalIgnoreCase) || IsTerminalRunStatus(run.Status))
            return ValueTask.FromResult(new ControlActionAvailabilityDto(ControlFacade.ActionKindPark, ControlFacade.ActionAvailabilityNotEligible, ControlFacade.ReasonStateNotEligible));

        // Permission is checked by attempting the control authorization.
        return ProjectParkAvailabilityPermissionAsync(run, cancellationToken);
    }

    private async ValueTask<ControlActionAvailabilityDto> ProjectParkAvailabilityPermissionAsync(
        RunSummaryDto run,
        CancellationToken cancellationToken)
    {
        try
        {
            await _workflowExternalControlAuthz.EnsureCanControlRunAsync(new WorkflowDefinitionId(run.ExecutionDefinitionId), cancellationToken).ConfigureAwait(false);
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindPark, ControlFacade.ActionAvailabilityAvailable, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindPark, ControlFacade.ActionAvailabilityUnavailable, ControlFacade.ReasonPermissionMissing);
        }
    }

    private async ValueTask<ControlActionAvailabilityDto> ProjectResumeAvailabilityAsync(
        RunSummaryDto run,
        bool hasQueuedResume,
        CancellationToken cancellationToken)
    {
        // Resume only valid when parked and no resume control is already queued.
        if (!string.Equals(run.Status, "parked", StringComparison.OrdinalIgnoreCase))
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindResume, ControlFacade.ActionAvailabilityNotEligible, ControlFacade.ReasonStateNotEligible);

        if (hasQueuedResume)
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindResume, ControlFacade.ActionAvailabilityAlreadyQueued, ControlFacade.ReasonAlreadyQueued);

        try
        {
            await _workflowExternalControlAuthz.EnsureCanControlRunAsync(new WorkflowDefinitionId(run.ExecutionDefinitionId), cancellationToken).ConfigureAwait(false);
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindResume, ControlFacade.ActionAvailabilityAvailable, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ControlActionAvailabilityDto(ControlFacade.ActionKindResume, ControlFacade.ActionAvailabilityUnavailable, ControlFacade.ReasonPermissionMissing);
        }
    }

    private static bool IsTerminalRunStatus(string status) =>
        string.Equals(status, WorkflowLifecycleStatusNames.Completed, StringComparison.Ordinal) ||
        string.Equals(status, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal) ||
        string.Equals(status, WorkflowLifecycleStatusNames.Cancelled, StringComparison.Ordinal) ||
        // Control-plane uses lowercase "cancelled" while workflow uses "cancelled/cancelled" tokens; normalize.
        string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool IsRetryableStatus(string status) =>
        string.Equals(status, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal) ||
        string.Equals(status, WorkflowLifecycleStatusNames.Cancelled, StringComparison.Ordinal) ||
        string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);
}

