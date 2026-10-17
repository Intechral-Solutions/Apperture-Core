using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// What an actor may do, as resolved from the actor itself: nothing (not a resolved human), everything (the legacy
/// break-glass session marker), or whatever an <see cref="EffectivePermissionSet"/> allows. The marker is a property of the
/// session, not of the user's roles, so it lives here and not in the set.
/// </summary>
public sealed class ActorAuthority
{
    private readonly EffectivePermissionSet? _set;

    private ActorAuthority(EffectivePermissionSet? set, bool isUnrestricted)
    {
        _set = set;
        IsUnrestricted = isUnrestricted;
    }

    /// <summary>The actor is not a human that resolves to a user, so nothing is allowed.</summary>
    public static ActorAuthority None { get; } = new(set: null, isUnrestricted: false);

    /// <summary>A resolved legacy break-glass session: every check is allowed.</summary>
    public static ActorAuthority Unrestricted { get; } = new(set: null, isUnrestricted: true);

    /// <summary>Authority that is exactly what <paramref name="set"/> allows.</summary>
    public static ActorAuthority FromSet(EffectivePermissionSet set) => new(set, isUnrestricted: false);

    /// <summary>True for a session that passes every check without consulting any role.</summary>
    public bool IsUnrestricted { get; }

    /// <summary>The evaluator's decision for these arguments.</summary>
    public bool Allows(PermissionDescriptor permission, ResourceDescriptor? resource, IGovernedResourceIdentity? resourceIdentity)
    {
        if (IsUnrestricted)
            return true;

        return _set is not null && _set.Allows(permission, resource, resourceIdentity);
    }

    /// <summary>True when the actor holds <paramref name="grant"/> (see <see cref="EffectivePermissionSet.Holds"/>).</summary>
    public bool Holds(PermissionDescriptor grant)
    {
        if (IsUnrestricted)
            return true;

        return _set is not null && _set.Holds(grant);
    }
}
