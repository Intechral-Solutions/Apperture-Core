using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Concrete IActor for human principals. Used when mapping ClaimsPrincipal to IActor.
/// When PreferredIdentitySource is set, the principal-to-user resolver uses only that source for lookup.
/// </summary>
public sealed class HumanActor : IActor
{
    public ActorId Id { get; }
    public ActorKind Kind => ActorKind.Human;
    public string? DisplayName { get; }

    /// <summary>
    /// When set, the resolver uses only this source for identity link lookup (no Entra-then-Local fallback).
    /// Epic 2 auth handlers should set this via the identity_source claim.
    /// </summary>
    public IdentitySource? PreferredIdentitySource { get; }

    public HumanActor(ActorId id, string? displayName = null, IdentitySource? preferredIdentitySource = null)
    {
        Id = id;
        DisplayName = displayName;
        PreferredIdentitySource = preferredIdentitySource;
    }
}
