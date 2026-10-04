using System.Security.Claims;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// The shape of a local (username and password) session identity: the one place it is defined, used by local sign-in,
/// native MFA completion, Recovery Access, the legacy break-glass endpoint (which adds its own claim) and the Entra
/// step-up callback that must re-issue a local session without the session cookie (a cross-site <c>form_post</c> does
/// not carry it; SEC-1 finding F8). The session is identified by the user id; see <see cref="ApplicationSessionClaims"/>.
/// </summary>
public static class LocalSessionIdentity
{
    /// <summary>Lifetime of a local session cookie (persistent, absolute at issue; the cookie handler slides it).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    /// <param name="userId">The user's id: the session identity.</param>
    /// <param name="username">The user's username at sign-in, for display only.</param>
    /// <param name="displayName">The user's display name.</param>
    public static ClaimsIdentity Create(UserId userId, string username, string displayName)
    {
        var identity = new ClaimsIdentity("Cookies");
        ApplicationSessionClaims.Apply(identity, userId, username, displayName, ApplicationSessionClaims.LocalSource);
        return identity;
    }
}
