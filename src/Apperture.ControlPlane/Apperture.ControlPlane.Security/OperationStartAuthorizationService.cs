using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Shared.Kernel;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Enforces operation start authorization using definition-level <c>RequiredPermissions</c> when present.
/// Otherwise falls back to the coarse executable permission model.
/// </summary>
public sealed class OperationStartAuthorizationService : IOperationStartAuthorizationService
{
    private static readonly string ExecuteAction = ActionConstants.OperationExecute;

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IOperationRegistryService _operations;
    private readonly IExecutablePermissionEvaluator _executablePermissionEvaluator;

    public OperationStartAuthorizationService(
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

    public async ValueTask EnsureCanStartOperationAsync(OperationId operationId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var definition = await _operations.GetOperationAsync(operationId, cancellationToken);
        if (definition?.RequiredPermissions is { Count: > 0 } requiredPermissions)
        {
            var allowedByDefinition = await _executablePermissionEvaluator.EvaluateAllAsync(actor, requiredPermissions, cancellationToken);
            if (!allowedByDefinition)
                throw new UnauthorizedAccessException("Not authorized to start this operation.");

            return;
        }

        // Coarse fallback: module-owned operations require `operation.execute` at module scope.
        // Otherwise require `operation.execute` at instance scope.
        var moduleId = definition?.ModuleId;
        var scopeKey = string.IsNullOrWhiteSpace(moduleId)
            ? ScopeKeyConstants.Instance
            : ScopeKeyConstants.Module(moduleId);

        var permission = new PermissionDescriptor(
            ExecuteAction,
            new ResourceDescriptor(ResourceTypeConstants.OperationDefinition, scopeKey));

        var allowed = await _auth.EvaluateAsync(
            actor,
            permission,
            permission.Resource,
            resourceIdentity: null,
            cancellationToken);

        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to start this operation.");
    }
}

