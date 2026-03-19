using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Cancel authorization for operation runs. Mirrors workflow-style definition-level semantics.
/// </summary>
public sealed class OperationRunCancelAuthorizationService : IOperationRunCancelAuthorizationService
{
    private const string ExecuteAction = ActionConstants.OperationExecute;

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IOperationRegistryService _operations;
    private readonly IExecutablePermissionEvaluator _executablePermissionEvaluator;

    public OperationRunCancelAuthorizationService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IOperationRegistryService operations,
        IExecutablePermissionEvaluator executablePermissionEvaluator)
    {
        _principalContext = principalContext;
        _auth = auth;
        _operations = operations;
        _executablePermissionEvaluator = executablePermissionEvaluator;
    }

    public async ValueTask EnsureCanCancelRunAsync(OperationId operationId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var definition = await _operations.GetOperationAsync(operationId, cancellationToken);
        if (definition?.RequiredPermissions is { Count: > 0 } requiredPermissions)
        {
            var allowedByDefinition = await _executablePermissionEvaluator.EvaluateAllAsync(actor, requiredPermissions, cancellationToken);
            if (!allowedByDefinition)
                throw new UnauthorizedAccessException("Not authorized to cancel this operation.");

            return;
        }

        // Coarse fallback: module-owned operations require `operation.execute` at module scope; otherwise at instance scope.
        var moduleId = definition?.ModuleId;
        var scopeKey = string.IsNullOrWhiteSpace(moduleId)
            ? ScopeKeyConstants.Instance
            : ScopeKeyConstants.Module(moduleId);

        var permission = new PermissionDescriptor(
            ExecuteAction,
            new ResourceDescriptor(ResourceTypeConstants.OperationDefinition, scopeKey));

        var allowedByCoarse = await _auth.EvaluateAsync(
            actor,
            permission,
            permission.Resource,
            resourceIdentity: null,
            cancellationToken);

        if (!allowedByCoarse)
            throw new UnauthorizedAccessException("Not authorized to cancel this operation.");
    }
}

