using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class OperationDefinitionReadAuthorizationService : IOperationDefinitionReadAuthorizationService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IOperationRegistryService _operations;

    public OperationDefinitionReadAuthorizationService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IOperationRegistryService operations)
    {
        _principalContext = principalContext;
        _auth = auth;
        _operations = operations;
    }

    public async ValueTask EnsureCanReadDefinitionAsync(OperationId operationId, CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var definition = await _operations.GetOperationAsync(operationId, cancellationToken);
        var scopeKey = string.IsNullOrWhiteSpace(definition?.ModuleId)
            ? ScopeKeyConstants.Instance
            : ScopeKeyConstants.Module(definition.ModuleId);

        var permission = new PermissionDescriptor(
            ActionConstants.OperationRead,
            new ResourceDescriptor(ResourceTypeConstants.OperationDefinition, scopeKey));

        var allowed = await _auth.EvaluateAsync(actor, permission, permission.Resource, resourceIdentity: null, cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to read this operation definition.");
    }
}

