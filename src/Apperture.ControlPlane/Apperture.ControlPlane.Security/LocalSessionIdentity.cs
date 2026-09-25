using System.Security.Claims;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// The shape of an ordinary local (username and password) session identity: the one place it is defined, used by local
/// sign-in and by the Entra step-up callback that must re-issue a local session without the session cookie (a cross-site
/// <c>form_post</c> does not carry it; SEC-1 finding F8). Emergency (break-glass) sessions add their own claim and are not
/// built here.
/// </summary>
public static class LocalSessionIdentity
{
    /// <summary>Lifetime of a local session cookie (persistent, absolute at issue; the cookie handler slides it).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    /// <param name="displayName">The user's display name.</param>
    /// <param name="normalizedUsername">The normalized local username: the local identity link's external id.</param>
    public static ClaimsIdentity Create(string displayName, string normalizedUsername)
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, normalizedUsername));
        identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local"));
        return identity;
    }
}
