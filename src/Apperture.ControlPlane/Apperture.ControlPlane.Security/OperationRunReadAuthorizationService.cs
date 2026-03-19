using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Run read authorization using definition-level <c>OperationDefinition.RequiredPermissions</c> when present.
/// Otherwise falls back to the coarse operation permissions (module-owned vs platform-owned).
/// </summary>
public sealed class OperationRunReadAuthorizationService : IOperationRunReadAuthorizationService
{
    private const string OperationReadAction = ActionConstants.OperationRead;

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IOperationRegistryService _operations;
    private readonly IExecutablePermissionEvaluator _executablePermissionEvaluator;

    public OperationRunReadAuthorizationService(
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

    public async ValueTask EnsureCanReadRunsAsync(OperationId operationId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var definition = await _operations.GetOperationAsync(operationId, cancellationToken);
        if (definition?.RequiredPermissions is { Count: > 0 } requiredPermissions)
        {
            var allowedByDefinition = await _executablePermissionEvaluator.EvaluateAllAsync(actor, requiredPermissions, cancellationToken);
            if (!allowedByDefinition)
                throw new UnauthorizedAccessException("Not authorized to read operation runs for this operation.");
            return;
        }

        var moduleId = definition?.ModuleId;
        var scopeKey = string.IsNullOrWhiteSpace(moduleId) ? ScopeKeyConstants.Instance : ScopeKeyConstants.Module(moduleId);

        var permission = new PermissionDescriptor(
            OperationReadAction,
            new ResourceDescriptor(ResourceTypeConstants.OperationDefinition, scopeKey));

        var allowedByCoarse = await _auth.EvaluateAsync(
            actor,
            permission,
            permission.Resource,
            resourceIdentity: null,
            cancellationToken);

        if (!allowedByCoarse)
            throw new UnauthorizedAccessException("Not authorized to read operation runs for this operation.");
    }
}

