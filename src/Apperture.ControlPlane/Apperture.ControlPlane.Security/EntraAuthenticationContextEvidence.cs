using System.Security.Claims;
using System.Text.Json;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Reads Microsoft's <c>acrs</c> claim from a validated id_token principal. This is evidence that
/// the configured authentication context was satisfied — not that a particular MFA method ran.
/// </summary>
public static class EntraAuthenticationContextEvidence
{
    public const string AcrsClaim = "acrs";

    /// <summary>
    /// True when the principal carries the specifically requested authentication-context identifier.
    /// </summary>
    public static bool Satisfies(ClaimsPrincipal? principal, string requestedContextId)
    {
        var requested = EntraAuthenticationContextId.TryNormalize(requestedContextId);
        if (requested is null)
            return false;

        foreach (var value in ReadReturnedContexts(principal))
        {
            if (string.Equals(value, requested, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Authentication-context identifiers asserted on the principal. Empty when Microsoft did not
    /// return <c>acrs</c>. Never uses <c>amr</c>, <c>acr</c>, or <c>auth_time</c>.
    /// </summary>
    public static IReadOnlyList<string> ReadReturnedContexts(ClaimsPrincipal? principal)
    {
        if (principal is null)
            return [];

        var values = new List<string>();
        foreach (var claim in principal.FindAll(AcrsClaim))
        {
            if (string.IsNullOrWhiteSpace(claim.Value))
                continue;
            AddParsed(values, claim.Value);
        }

        return values;
    }

    private static void AddParsed(List<string> values, string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(trimmed);
                if (parsed is not null)
                {
                    foreach (var item in parsed)
                    {
                        var normalized = EntraAuthenticationContextId.TryNormalize(item);
                        if (normalized is not null && !values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                            values.Add(normalized);
                    }

                    return;
                }
            }
            catch (JsonException)
            {
                // Fall through to delimiter splitting.
            }
        }

        foreach (var part in trimmed.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = EntraAuthenticationContextId.TryNormalize(part);
            if (normalized is not null && !values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                values.Add(normalized);
        }
    }
}
