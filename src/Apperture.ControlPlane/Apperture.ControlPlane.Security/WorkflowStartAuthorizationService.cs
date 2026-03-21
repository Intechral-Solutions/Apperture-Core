using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Enforces workflow start authorization: workflow.execute at module scope when the workflow is module-owned,
/// otherwise workflow.start at instance scope.
/// </summary>
public sealed class WorkflowStartAuthorizationService : IWorkflowStartAuthorizationService
{
    private const string WorkflowExecuteAction = "workflow.execute";

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IWorkflowModuleResolver _workflowModuleResolver;
    private readonly IWorkflowDefinitionResolver _workflowDefinitionResolver;
    private readonly IExecutablePermissionEvaluator _executablePermissionEvaluator;

    public WorkflowStartAuthorizationService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IWorkflowModuleResolver workflowModuleResolver,
        IWorkflowDefinitionResolver workflowDefinitionResolver,
        IExecutablePermissionEvaluator executablePermissionEvaluator)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _workflowModuleResolver = workflowModuleResolver;
        _workflowDefinitionResolver = workflowDefinitionResolver;
        _executablePermissionEvaluator = executablePermissionEvaluator;
    }

    /// <inheritdoc />
    public async ValueTask EnsureCanStartWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var definition = await _workflowDefinitionResolver.GetByIdAsync(workflowDefinitionId, cancellationToken);
        if (definition is null)
            throw new UnauthorizedAccessException("Workflow not found or not available.");

        if (definition.RequiredPermissions is { Count: > 0 } requiredPermissions)
        {
            var allowedByDefinition = await _executablePermissionEvaluator.EvaluateAllAsync(actor, requiredPermissions, cancellationToken);
            if (!allowedByDefinition)
                throw new UnauthorizedAccessException("Not authorized to start this workflow.");
            return;
        }

        var moduleId = await _workflowModuleResolver.GetModuleIdForWorkflowAsync(workflowDefinitionId, cancellationToken);
        bool allowed;
        if (moduleId is not null)
        {
            var moduleScopeKey = ScopeKeyConstants.Module(moduleId);
            var permission = new PermissionDescriptor(WorkflowExecuteAction, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, moduleScopeKey));
            allowed = await _authorizationEvaluation.EvaluateAsync(
                new AuthorizationEvaluationContext(actor, permission, permission.Resource),
                cancellationToken);
        }
        else
        {
            var permission = new PermissionDescriptor(ActionConstants.WorkflowStart, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance));
            allowed = await _authorizationEvaluation.EvaluateAsync(
                new AuthorizationEvaluationContext(actor, permission, permission.Resource),
                cancellationToken);
        }

        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to start this workflow.");
    }
}
