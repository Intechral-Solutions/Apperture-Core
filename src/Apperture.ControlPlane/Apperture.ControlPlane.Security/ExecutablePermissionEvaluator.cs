using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class ExecutablePermissionEvaluator : IExecutablePermissionEvaluator
{
    private readonly IAuthorizationService _auth;
    private readonly IResourceDescriptorRegistry _registry;

    public ExecutablePermissionEvaluator(IAuthorizationService auth, IResourceDescriptorRegistry registry)
    {
        _auth = auth;
        _registry = registry;
    }

    public async ValueTask<bool> EvaluateAllAsync(
        IActor actor,
        IReadOnlyList<PermissionDescriptor> requiredPermissions,
        CancellationToken cancellationToken = default)
    {
        foreach (var permission in requiredPermissions)
        {
            if (!IsKnown(permission))
                return false;

            // IMPORTANT: pass resource so RBAC evaluator uses the correct target scope.
            var allowed = await _auth.EvaluateAsync(
                actor,
                permission,
                permission.Resource,
                null,
                cancellationToken);
            if (!allowed)
                return false;
        }

        return true;
    }

    private bool IsKnown(PermissionDescriptor required)
    {
        // Conservative match:
        // - required.Resource is null: require at least one catalog permission with matching action
        // - required.Resource is set: require catalog permission with matching action + resourceType + scopeKey
        var catalog = _registry.GetPermissionDescriptors(null);
        if (required.Resource is null)
        {
            return catalog.Any(p => string.Equals(p.Action, required.Action, StringComparison.Ordinal));
        }

        return catalog.Any(p =>
            string.Equals(p.Action, required.Action, StringComparison.Ordinal) &&
            p.Resource is not null &&
            string.Equals(p.Resource.ResourceType, required.Resource.ResourceType, StringComparison.Ordinal) &&
            string.Equals(p.Resource.ScopeKey, required.Resource.ScopeKey, StringComparison.Ordinal));
    }
}

