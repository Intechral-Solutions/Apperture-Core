using System.Text.RegularExpressions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Microsoft Entra Conditional Access authentication-context identifiers are <c>c1</c> through <c>c25</c>.
/// Apperture stores the identifier and trusts Microsoft's assertion that it was satisfied; it does not
/// interpret the identifier as a particular MFA technology.
/// </summary>
public static partial class EntraAuthenticationContextId
{
    /// <summary>
    /// Returns a normalized identifier (<c>c1</c>–<c>c25</c>, lowercase) or null when the value is empty or not a valid authentication-context id.
    /// </summary>
    public static string? TryNormalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim().ToLowerInvariant();
        return IdentifierPattern().IsMatch(trimmed) ? trimmed : null;
    }

    public static bool IsValid(string? value) => TryNormalize(value) is not null;

    [GeneratedRegex("^c([1-9]|1[0-9]|2[0-5])$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
