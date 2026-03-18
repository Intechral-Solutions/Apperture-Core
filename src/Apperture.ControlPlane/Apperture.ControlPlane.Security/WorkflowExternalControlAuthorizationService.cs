using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// External-control authorization using the executable permission model:
/// - If the workflow definition declares RequiredPermissions, ALL are required for control.
/// - Otherwise, fall back to coarse workflow permissions (module-owned -> workflow.execute at module scope; else workflow.start at instance).
/// </summary>
public sealed class WorkflowExternalControlAuthorizationService : IWorkflowExternalControlAuthorizationService
{
    private const string WorkflowExecuteAction = "workflow.execute";

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IWorkflowDefinitionResolver _definitionResolver;
    private readonly IExecutablePermissionEvaluator _executablePermissionEvaluator;
    private readonly IWorkflowModuleResolver _workflowModuleResolver;

    public WorkflowExternalControlAuthorizationService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IWorkflowDefinitionResolver definitionResolver,
        IExecutablePermissionEvaluator executablePermissionEvaluator,
        IWorkflowModuleResolver workflowModuleResolver)
    {
        _principalContext = principalContext;
        _auth = auth;
        _definitionResolver = definitionResolver;
        _executablePermissionEvaluator = executablePermissionEvaluator;
        _workflowModuleResolver = workflowModuleResolver;
    }

    public async ValueTask EnsureCanControlRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var definition = await _definitionResolver.GetByIdAsync(workflowDefinitionId, cancellationToken);
        if (definition?.RequiredPermissions is { Count: > 0 } requiredPermissions)
        {
            var allowedByDefinition = await _executablePermissionEvaluator.EvaluateAllAsync(actor, requiredPermissions, cancellationToken);
            if (!allowedByDefinition)
                throw new UnauthorizedAccessException("Not authorized to control this workflow.");
            return;
        }

        var moduleId = await _workflowModuleResolver.GetModuleIdForWorkflowAsync(workflowDefinitionId, cancellationToken);
        bool allowed;
        if (moduleId is not null)
        {
            var moduleScopeKey = ScopeKeyConstants.Module(moduleId);
            var permission = new PermissionDescriptor(WorkflowExecuteAction, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, moduleScopeKey));
            allowed = await _auth.EvaluateAsync(
                actor,
                permission,
                permission.Resource,
                null,
                cancellationToken);
        }
        else
        {
            var permission = new PermissionDescriptor(ActionConstants.WorkflowStart, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance));
            allowed = await _auth.EvaluateAsync(
                actor,
                permission,
                permission.Resource,
                null,
                cancellationToken);
        }

        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to control this workflow.");
    }
}
