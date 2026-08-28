using System.Globalization;
using System.Security.Claims;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Apperture-namespaced cookie claims that record recent strong authentication.
/// The cookie is issued by the host and protected by ASP.NET Data Protection; the UI cannot
/// set or forge these values.
/// </summary>
public static class AuthenticationAssuranceClaims
{
    public const string MechanismClaimType = "apperture.auth.assurance.mechanism";
    public const string ContextClaimType = "apperture.auth.assurance.context";
    public const string EstablishedAtClaimType = "apperture.auth.assurance.at";
    public const string SimulatedClaimType = "apperture.auth.assurance.simulated";

    public const string EntraAuthenticationContextMechanism = "entra-authentication-context";

    /// <summary>Round-trip format that is independent of the server locale.</summary>
    public const string TimestampFormat = "o";

    public static void Replace(ClaimsIdentity identity, string mechanism, DateTimeOffset establishedAtUtc, string? providerContext, bool developmentSimulated = false)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Remove(identity);
        identity.AddClaim(new Claim(MechanismClaimType, mechanism));
        identity.AddClaim(new Claim(EstablishedAtClaimType, establishedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture)));
        if (!string.IsNullOrWhiteSpace(providerContext))
            identity.AddClaim(new Claim(ContextClaimType, providerContext.Trim()));
        if (developmentSimulated)
            identity.AddClaim(new Claim(SimulatedClaimType, "1"));
    }

    public static void Remove(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        foreach (var claim in identity.FindAll(c =>
                     c.Type is MechanismClaimType or ContextClaimType or EstablishedAtClaimType or SimulatedClaimType).ToList())
        {
            identity.RemoveClaim(claim);
        }
    }

    public static DateTimeOffset? TryGetEstablishedAt(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(EstablishedAtClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTimeOffset.TryParseExact(
            raw,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    public static string? TryGetMechanism(ClaimsPrincipal? principal) =>
        EmptyToNull(principal?.FindFirst(MechanismClaimType)?.Value);

    public static string? TryGetContext(ClaimsPrincipal? principal) =>
        EmptyToNull(principal?.FindFirst(ContextClaimType)?.Value);

    public static bool IsDevelopmentSimulated(ClaimsPrincipal? principal) =>
        string.Equals(principal?.FindFirst(SimulatedClaimType)?.Value, "1", StringComparison.Ordinal);

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
