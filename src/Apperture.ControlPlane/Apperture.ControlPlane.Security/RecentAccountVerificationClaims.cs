using System.Globalization;
using System.Security.Claims;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Cookie claims for recent account verification. Separate from strong-authentication claims so a
/// local-password unlock never pretends to be MFA.
/// </summary>
public static class RecentAccountVerificationClaims
{
    public const string MechanismClaimType = "apperture.auth.account.verification.mechanism";
    public const string EstablishedAtClaimType = "apperture.auth.account.verification.at";

    public static void Replace(ClaimsIdentity identity, string mechanism, DateTimeOffset establishedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Remove(identity);
        identity.AddClaim(new Claim(MechanismClaimType, mechanism));
        identity.AddClaim(new Claim(
            EstablishedAtClaimType,
            establishedAtUtc.ToUniversalTime().ToString(AuthenticationAssuranceClaims.TimestampFormat, CultureInfo.InvariantCulture)));
    }

    public static void Remove(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        foreach (var claim in identity.FindAll(c =>
                     c.Type is MechanismClaimType or EstablishedAtClaimType).ToList())
        {
            identity.RemoveClaim(claim);
        }
    }

    public static string? TryGetMechanism(ClaimsPrincipal? principal) =>
        string.IsNullOrWhiteSpace(principal?.FindFirst(MechanismClaimType)?.Value)
            ? null
            : principal!.FindFirst(MechanismClaimType)!.Value.Trim();

    public static DateTimeOffset? TryGetEstablishedAt(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(EstablishedAtClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTimeOffset.TryParseExact(
            raw,
            AuthenticationAssuranceClaims.TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
