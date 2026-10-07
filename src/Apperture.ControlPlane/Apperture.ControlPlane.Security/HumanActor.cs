using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Concrete IActor for human principals, produced from an application session by
/// <see cref="ClaimsPrincipalToActorMapper"/>. <see cref="Id"/> is the user id. The identity source and the
/// Entra tenant and object ids describe the identity link the session was issued through, so the resolver can
/// require that link to still belong to the user.
/// </summary>
public sealed class HumanActor : IActor, IRecoveryAccessSession
{
    public ActorId Id { get; }
    public ActorKind Kind => ActorKind.Human;
    public string? DisplayName { get; }

    /// <summary>
    /// The identity source the session was established with (the <c>identity_source</c> claim). The resolver
    /// binds the session to an identity link of this source.
    /// </summary>
    public IdentitySource? PreferredIdentitySource { get; }

    /// <summary>
    /// Directory (tenant) id from a validated Microsoft token. With <see cref="EntraObjectId"/>, the Entra link
    /// an Entra session was issued through.
    /// </summary>
    public string? TenantId { get; }

    /// <summary>Directory object id (<c>oid</c>) of an Entra session. Link provenance only, never the actor id.</summary>
    public string? EntraObjectId { get; }

    /// <summary>
    /// True when the session was established via the break-glass login endpoint. The authorization evaluator grants all permission checks for such sessions.
    /// </summary>
    public bool IsBreakGlassSession { get; }

    /// <inheritdoc />
    bool IRecoveryAccessSession.IsRecoveryAccessSession => IsBreakGlassSession;

    public HumanActor(
        ActorId id,
        string? displayName = null,
        IdentitySource? preferredIdentitySource = null,
        bool isBreakGlassSession = false,
        string? tenantId = null,
        string? entraObjectId = null)
    {
        Id = id;
        DisplayName = displayName;
        PreferredIdentitySource = preferredIdentitySource;
        IsBreakGlassSession = isBreakGlassSession;
        TenantId = tenantId;
        EntraObjectId = entraObjectId;
    }
}
