using System.Text;

namespace Apperture.Core.Domain.ResourceContexts;

/// <summary>
/// Deterministic normalization and validation for ResourceContext slugs.
/// </summary>
/// <remarks>
/// A slug is a human-friendly address/namespace value. It is <b>never</b> identity: every stored
/// reference and every route uses the ResourceContext id. Only normalized slugs are persisted, and
/// slugs are unique (case-insensitively) across all ResourceContexts.
/// </remarks>
public static class ResourceContextSlug
{
    /// <summary>Maximum persisted slug length.</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Slugs that may not be taken by an operator-created ResourceContext.
    /// </summary>
    /// <remarks>
    /// Deliberately minimal. <c>shared</c> belongs to the reserved Shared ResourceContext and
    /// <c>system</c> is withheld for platform use. Speculative route terms are not reserved until
    /// a slug-addressed route actually needs them.
    /// </remarks>
    public static IReadOnlySet<string> ReservedSlugs { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "shared", "system" };

    /// <summary>
    /// Normalizes a candidate slug: lowercase, trim, whitespace and underscores to hyphens,
    /// unsupported characters removed, repeated hyphens collapsed, leading/trailing hyphens trimmed.
    /// </summary>
    /// <returns>The normalized slug.</returns>
    /// <exception cref="ArgumentException">The candidate normalizes to an empty value.</exception>
    public static string Normalize(string? candidate)
    {
        if (!TryNormalize(candidate, out var normalized))
            throw new ArgumentException($"'{candidate}' does not contain any characters usable in a ResourceContext slug.", nameof(candidate));
        return normalized;
    }

    /// <summary>
    /// Normalizes a candidate slug. Returns false when the result would be empty.
    /// </summary>
    public static bool TryNormalize(string? candidate, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var builder = new StringBuilder(candidate.Length);
        foreach (var raw in candidate.Trim())
        {
            var ch = char.ToLowerInvariant(raw);
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(ch);
                continue;
            }

            // Whitespace, underscore and hyphen all collapse to a single separator.
            if (ch == '-' || ch == '_' || char.IsWhiteSpace(ch))
            {
                if (builder.Length > 0 && builder[^1] != '-')
                    builder.Append('-');
                continue;
            }

            // Everything else is unsupported and is removed, not transliterated.
        }

        while (builder.Length > 0 && builder[^1] == '-')
            builder.Length--;

        if (builder.Length == 0)
            return false;

        normalized = builder.Length > MaxLength
            ? builder.ToString(0, MaxLength).TrimEnd('-')
            : builder.ToString();

        return normalized.Length > 0;
    }

    /// <summary>True when the normalized slug is reserved and may not be assigned to a new ResourceContext.</summary>
    public static bool IsReserved(string? normalizedSlug) =>
        normalizedSlug is not null && ReservedSlugs.Contains(normalizedSlug);
}
