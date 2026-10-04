using System.Security.Claims;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Maps an application-session <see cref="ClaimsPrincipal"/> to a human <see cref="IActor"/> whose id is the
/// session's user id (<see cref="ApplicationSessionClaims"/>). Local, Entra and Recovery Access sessions of one
/// user therefore produce the same actor id. The identity source, the break-glass marker and the Entra
/// <c>tid</c> and <c>oid</c> ride along as provenance so the resolver can check the session's identity link
/// still exists; they are never the actor id.
/// </summary>
public static class ClaimsPrincipalToActorMapper
{
    /// <summary>
    /// Namespace for claims this application itself issues (identity source, break-glass, and any later session metadata).
    /// An external identity provider must never be allowed to assert a claim in this namespace.
    /// </summary>
    public const string ApplicationClaimNamespace = "http://schemas.apperture/";

    /// <summary>
    /// Claim set by auth handler to indicate identity source (Entra or Local). When present, resolver uses only that source.
    /// </summary>
    public const string IdentitySourceClaimType = ApplicationClaimNamespace + "identity_source";

    /// <summary>
    /// Claim set when the session was established via break-glass login. Value "1" indicates break-glass session; evaluator grants all permission checks.
    /// </summary>
    public const string BreakGlassClaimType = ApplicationClaimNamespace + "break_glass";

    /// <summary>
    /// Removes every claim whose type is in <see cref="ApplicationClaimNamespace"/>.
    /// Call this on a principal that just arrived from an external identity provider, before this host adds its own claims in that namespace.
    /// </summary>
    public static void RemoveUntrustedApplicationClaims(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        foreach (var claim in identity.Claims
                     .Where(c => c.Type.StartsWith(ApplicationClaimNamespace, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            identity.RemoveClaim(claim);
        }
    }

    /// <summary>
    /// Maps an authenticated application session to a human actor whose id is the session's user id, or returns
    /// null. A principal that is not in the current session format (<see cref="ApplicationSessionClaims"/>), has no
    /// <see cref="ClaimTypes.NameIdentifier"/>, or has no recognised identity source is not an application session
    /// and maps to no actor. There is deliberately no fallback to <c>oid</c>, <c>sub</c>, a name or a username.
    /// </summary>
    public static IActor? ToActor(ClaimsPrincipal? principal)
    {
        if (principal is null || principal.Identity?.IsAuthenticated != true)
            return null;
        if (!ApplicationSessionClaims.IsCurrentFormat(principal))
            return null;

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var sourceClaim = principal.FindFirst(IdentitySourceClaimType)?.Value;
        IdentitySource source;
        if (string.Equals(sourceClaim, ApplicationSessionClaims.EntraSource, StringComparison.OrdinalIgnoreCase))
            source = IdentitySource.Entra;
        else if (string.Equals(sourceClaim, ApplicationSessionClaims.LocalSource, StringComparison.OrdinalIgnoreCase))
            source = IdentitySource.Local;
        else
            return null;

        var isBreakGlassSession = string.Equals(principal.FindFirst(BreakGlassClaimType)?.Value, "1", StringComparison.Ordinal);

        // Provenance of an Entra session: the link it was issued through. Never the actor id.
        string? tenantId = null;
        string? objectId = null;
        if (source == IdentitySource.Entra)
        {
            tenantId = EntraTokenClaims.TryGetTenantId(principal);
            objectId = EntraTokenClaims.TryGetObjectId(principal);
        }

        return new HumanActor(
            new ActorId(userId),
            principal.FindFirst(ClaimTypes.Name)?.Value,
            source,
            isBreakGlassSession,
            tenantId,
            objectId);
    }
}
