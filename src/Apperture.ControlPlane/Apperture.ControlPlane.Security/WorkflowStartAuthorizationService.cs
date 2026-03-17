using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
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
    private readonly IAuthorizationService _auth;
    private readonly IWorkflowModuleResolver _workflowModuleResolver;

    public WorkflowStartAuthorizationService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IWorkflowModuleResolver workflowModuleResolver)
    {
        _principalContext = principalContext;
        _auth = auth;
        _workflowModuleResolver = workflowModuleResolver;
    }

    /// <inheritdoc />
    public async ValueTask EnsureCanStartWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var moduleId = await _workflowModuleResolver.GetModuleIdForWorkflowAsync(workflowDefinitionId, cancellationToken);
        bool allowed;
        if (moduleId is not null)
        {
            var moduleScopeKey = ScopeKeyConstants.Module(moduleId);
            allowed = await _auth.EvaluateAsync(
                actor,
                new PermissionDescriptor(WorkflowExecuteAction, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, moduleScopeKey)),
                null,
                null,
                cancellationToken);
        }
        else
        {
            allowed = await _auth.EvaluateAsync(
                actor,
                new PermissionDescriptor(ActionConstants.WorkflowStart, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance)),
                null,
                null,
                cancellationToken);
        }

        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to start this workflow.");
    }
}
