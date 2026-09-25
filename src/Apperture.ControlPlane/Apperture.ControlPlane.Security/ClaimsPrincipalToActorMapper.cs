using System.Security.Claims;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Maps ClaimsPrincipal to IActor (Human). Provider-agnostic; uses standard claim types
/// so both Entra and local auth can populate them later.
/// For Entra, Id (ExternalId for identity link) must be the stable subject: oid or sub—not preferred_username or email.
/// </summary>
public static class ClaimsPrincipalToActorMapper
{
    /// <summary>
    /// Azure AD / Entra object ID (immutable). Prefer this for IdentityLink.ExternalId when present.
    /// </summary>
    private static readonly string[] OidClaimTypes = [
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
        "oid" // JWT from Entra uses short name
    ];

    /// <summary>
    /// JWT/OIDC claim types (fallback when ASP.NET claim types not present).
    /// </summary>
    private const string JwtSub = "sub";
    private const string JwtPreferredUsername = "preferred_username";
    private const string JwtName = "name";

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
    /// Maps an authenticated ClaimsPrincipal to a Human IActor, or returns null if not authenticated.
    /// Id (used as ExternalId for lookup): oid first (Entra stable), then NameIdentifier, sub, name, preferred_username.
    /// Epic 2: set identity_source claim so resolution is unambiguous.
    /// </summary>
    public static IActor? ToActor(ClaimsPrincipal? principal)
    {
        if (principal is null || principal.Identity?.IsAuthenticated != true)
            return null;

        // Stable subject for Entra: oid or sub; do not use preferred_username/email as primary ExternalId.
        var id = OidClaimTypes.Select(t => principal.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrEmpty(v))
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(JwtSub)?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value
            ?? principal.FindFirst(JwtPreferredUsername)?.Value;
        if (string.IsNullOrEmpty(id))
            return null;

        var displayName = principal.FindFirst(ClaimTypes.Name)?.Value
            ?? principal.FindFirst(JwtName)?.Value
            ?? principal.FindFirst(JwtPreferredUsername)?.Value;

        IdentitySource? preferredSource = null;
        var sourceClaim = principal.FindFirst(IdentitySourceClaimType)?.Value;
        if (string.Equals(sourceClaim, "Entra", StringComparison.OrdinalIgnoreCase))
            preferredSource = IdentitySource.Entra;
        else if (string.Equals(sourceClaim, "Local", StringComparison.OrdinalIgnoreCase))
            preferredSource = IdentitySource.Local;

        var breakGlassClaim = principal.FindFirst(BreakGlassClaimType)?.Value;
        var isBreakGlassSession = string.Equals(breakGlassClaim, "1", StringComparison.Ordinal);

        var tenantId = preferredSource == IdentitySource.Entra
            ? EntraTokenClaims.TryGetTenantId(principal)
            : null;

        return new HumanActor(new ActorId(id), displayName, preferredSource, isBreakGlassSession, tenantId);
    }
}
