using System.Security.Claims;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// The canonical identity of every application-cookie session: <see cref="ClaimTypes.NameIdentifier"/> is the
/// stable <see cref="UserId"/>, marked with <see cref="SessionFormatClaimType"/> = <see cref="CurrentSessionFormat"/>.
/// Local, Entra, Recovery Access and step-up sessions all carry the same shape; only the identity source and the
/// provider's own claims (Entra <c>tid</c> and <c>oid</c>) differ, and those are link provenance, never identity.
/// </summary>
/// <remarks>
/// Every claim type here is in <see cref="ClaimsPrincipalToActorMapper.ApplicationClaimNamespace"/> or is a
/// framework identity claim this host replaces, so an identity provider can never supply one: the Entra handler
/// strips the namespace and the incoming <see cref="ClaimTypes.NameIdentifier"/> before these are added.
/// </remarks>
public static class ApplicationSessionClaims
{
    /// <summary>Marks a session issued with the user-id identity. Sessions without it are rejected at the cookie.</summary>
    public const string SessionFormatClaimType = ClaimsPrincipalToActorMapper.ApplicationClaimNamespace + "session_format";

    public const string CurrentSessionFormat = "2";

    /// <summary>
    /// The username at sign-in, for display only. It goes stale after a rename and is never used to identify
    /// or authorize anyone.
    /// </summary>
    public const string UsernameClaimType = ClaimsPrincipalToActorMapper.ApplicationClaimNamespace + "username";

    public const string LocalSource = "Local";
    public const string EntraSource = "Entra";

    /// <summary>True when the principal was issued in the current session format.</summary>
    public static bool IsCurrentFormat(ClaimsPrincipal? principal) =>
        string.Equals(principal?.FindFirst(SessionFormatClaimType)?.Value, CurrentSessionFormat, StringComparison.Ordinal);

    /// <summary>
    /// Sets the canonical session identity on <paramref name="identity"/>, replacing any existing claim of the same
    /// types: user id, display name, identity source, username and session format.
    /// </summary>
    public static void Apply(ClaimsIdentity identity, UserId userId, string username, string displayName, string identitySource)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId.Value);

        Replace(identity, ClaimTypes.NameIdentifier, userId.Value);
        Replace(identity, ClaimTypes.Name, displayName);
        Replace(identity, ClaimsPrincipalToActorMapper.IdentitySourceClaimType, identitySource);
        Replace(identity, UsernameClaimType, username);
        Replace(identity, SessionFormatClaimType, CurrentSessionFormat);
    }

    private static void Replace(ClaimsIdentity identity, string type, string value)
    {
        foreach (var existing in identity.FindAll(type).ToList())
            identity.RemoveClaim(existing);
        identity.AddClaim(new Claim(type, value));
    }
}
