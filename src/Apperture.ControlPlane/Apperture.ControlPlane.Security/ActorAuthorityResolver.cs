using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves an actor to its <see cref="ActorAuthority"/> with the rules the authorization evaluator has always applied:
/// only a <see cref="ActorKind.Human"/> actor gets role-based authority, and only once it resolves to its user through the
/// identity link it was issued through; a resolved legacy break-glass session is unrestricted; everything else has none.
/// </summary>
public sealed class ActorAuthorityResolver
{
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;
    private readonly IScopeContainmentEvaluator _scopeEvaluator;

    public ActorAuthorityResolver(
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider,
        IScopeContainmentEvaluator scopeEvaluator)
    {
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
        _scopeEvaluator = scopeEvaluator;
    }

    public async ValueTask<ActorAuthority> ResolveAsync(IActor actor, CancellationToken cancellationToken = default)
    {
        if (actor is null)
            return ActorAuthority.None;

        // Machine, System, RemoteInstance: deny by default
        if (actor.Kind != ActorKind.Human)
            return ActorAuthority.None;

        // Every human session, break-glass included, must first resolve to its user through the identity link
        // it was issued through. A session whose link is gone authorizes nothing.
        var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
        if (userId is null)
            return ActorAuthority.None;

        // Break-glass: a resolved session established via break-glass login grants all permission checks
        if (actor is HumanActor { IsBreakGlassSession: true })
            return ActorAuthority.Unrestricted;

        var roles = await _roleProvider.GetRolesForUserAsync(userId, cancellationToken);
        return ActorAuthority.FromSet(EffectivePermissionSet.FromRoles(roles, _scopeEvaluator));
    }
}
