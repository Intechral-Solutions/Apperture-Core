using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Implements IAuthorizationPolicyEvaluator using RBAC. Per ADR-0012: break-glass and relationship slots reserved.
/// Human actors with resolved UserId get role-based evaluation; Machine/System/RemoteInstance deny by default.
/// A break-glass session bypasses role evaluation only after it has resolved to its user.
/// What the roles allow is defined by <see cref="EffectivePermissionSet"/> and the actor handling by
/// <see cref="ActorAuthorityResolver"/>; this type only joins them to the evaluator contract.
/// </summary>
public sealed class RbacAuthorizationPolicyEvaluator : IAuthorizationPolicyEvaluator
{
    private readonly ActorAuthorityResolver _authorityResolver;

    public RbacAuthorizationPolicyEvaluator(
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider,
        IScopeContainmentEvaluator scopeEvaluator)
    {
        _authorityResolver = new ActorAuthorityResolver(principalToUserResolver, roleProvider, scopeEvaluator);
    }

    /// <inheritdoc />
    public async ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        IGovernedResourceIdentity? resourceIdentity,
        CancellationToken cancellationToken = default)
    {
        // Relationship slot: reserved, no-op

        var authority = await _authorityResolver.ResolveAsync(actor, cancellationToken);
        return authority.Allows(permission, resource, resourceIdentity);
    }
}
