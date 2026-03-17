using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Run read authorization using the executable permission model:
/// - If the workflow definition declares RequiredPermissions, ALL are required for read.
/// - Otherwise, fall back to coarse workflow permissions (module-owned -> workflow.execute at module scope; else workflow.start at instance).
/// </summary>
public sealed class WorkflowRunReadAuthorizationService : IWorkflowRunReadAuthorizationService
{
    private const string WorkflowExecuteAction = "workflow.execute";

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IWorkflowDefinitionResolver _definitionResolver;
    private readonly IExecutablePermissionEvaluator _executablePermissionEvaluator;
    private readonly IWorkflowModuleResolver _workflowModuleResolver;

    public WorkflowRunReadAuthorizationService(
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

    public async ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var definition = await _definitionResolver.GetByIdAsync(workflowDefinitionId, cancellationToken);
        if (definition?.RequiredPermissions is { Count: > 0 } required)
        {
            var allowed = await _executablePermissionEvaluator.EvaluateAllAsync(actor, required, cancellationToken);
            if (!allowed)
                throw new UnauthorizedAccessException("Not authorized to read runs for this workflow.");
            return;
        }

        var moduleId = await _workflowModuleResolver.GetModuleIdForWorkflowAsync(workflowDefinitionId, cancellationToken);
        bool fallbackAllowed;
        if (moduleId is not null)
        {
            var scopeKey = ScopeKeyConstants.Module(moduleId);
            var permission = new PermissionDescriptor(WorkflowExecuteAction, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, scopeKey));
            fallbackAllowed = await _auth.EvaluateAsync(actor, permission, permission.Resource, null, cancellationToken);
        }
        else
        {
            var permission = new PermissionDescriptor(ActionConstants.WorkflowStart, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance));
            fallbackAllowed = await _auth.EvaluateAsync(actor, permission, permission.Resource, null, cancellationToken);
        }

        if (!fallbackAllowed)
            throw new UnauthorizedAccessException("Not authorized to read runs for this workflow.");
    }
}

