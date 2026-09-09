using System.Security.Claims;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Limited pre-provisioning cookie claims. This principal is not an Apperture user and cannot
/// satisfy ordinary application authorization.
/// </summary>
public static class EntraAccessRequestAuthentication
{
    public const string Scheme = "Apperture.EntraAccessRequest";
    public const string CookieName = ".Apperture.EntraAccessRequest";
    public const string SessionKindClaim = "apperture.auth.session_kind";
    public const string SessionKindValue = "entra-access-request";
    public const string RegistrationIdClaim = "apperture.auth.entra.registration_id";
    public const string AuthenticatedAtClaim = "apperture.auth.entra.authenticated_at";
    public const string DisplayNameClaim = "apperture.auth.entra.display_name";
    public const string UpnClaim = "apperture.auth.entra.upn";
    public const string MailClaim = "apperture.auth.entra.mail";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    public static ClaimsPrincipal CreatePrincipal(
        string tenantId,
        string objectId,
        string registrationId,
        DateTimeOffset authenticatedAtUtc,
        string? displayName,
        string? upn,
        string? mail)
    {
        var identity = new ClaimsIdentity(Scheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, objectId));
        identity.AddClaim(new Claim("oid", objectId));
        identity.AddClaim(new Claim("tid", tenantId));
        identity.AddClaim(new Claim(RegistrationIdClaim, registrationId));
        identity.AddClaim(new Claim(SessionKindClaim, SessionKindValue));
        identity.AddClaim(new Claim(AuthenticatedAtClaim, authenticatedAtUtc.ToString("O")));
        if (!string.IsNullOrWhiteSpace(displayName))
            identity.AddClaim(new Claim(DisplayNameClaim, displayName.Trim()));
        if (!string.IsNullOrWhiteSpace(upn))
            identity.AddClaim(new Claim(UpnClaim, upn.Trim()));
        if (!string.IsNullOrWhiteSpace(mail))
            identity.AddClaim(new Claim(MailClaim, mail.Trim()));
        return new ClaimsPrincipal(identity);
    }

    public static EntraAccessRequestPrincipal? TryRead(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not { IsAuthenticated: true } identity)
            return null;
        if (!string.Equals(identity.AuthenticationType, Scheme, StringComparison.Ordinal)
            && principal.FindFirst(SessionKindClaim)?.Value != SessionKindValue)
        {
            return null;
        }

        var objectId = EntraTokenClaims.Normalize(principal.FindFirst("oid")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        var tenantId = EntraTokenClaims.Normalize(principal.FindFirst("tid")?.Value);
        var registrationId = principal.FindFirst(RegistrationIdClaim)?.Value;
        if (!EntraTokenClaims.IsValidObjectId(objectId)
            || !EntraTokenClaims.IsValidObjectId(tenantId)
            || string.IsNullOrWhiteSpace(registrationId))
        {
            return null;
        }

        var authenticatedAt = DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(principal.FindFirst(AuthenticatedAtClaim)?.Value, out var parsed))
            authenticatedAt = parsed;

        return new EntraAccessRequestPrincipal(
            tenantId!,
            objectId!,
            registrationId.Trim(),
            authenticatedAt,
            principal.FindFirst(DisplayNameClaim)?.Value,
            principal.FindFirst(UpnClaim)?.Value,
            principal.FindFirst(MailClaim)?.Value);
    }
}
