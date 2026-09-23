namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Wording for a Module's <c>ContributionCounts</c> (workflows, operations, permissions, navigation). This is
/// Module metadata: none of these are Component kinds, and nothing here implies they are.
/// </summary>
public static class ModuleContributionPresentation
{
    private static readonly (string Kind, string Singular, string Plural)[] Known =
    [
        ("workflow", "workflow", "workflows"),
        ("operation", "operation", "operations"),
        ("permission", "permission", "permissions"),
        ("navigation", "navigation item", "navigation items"),
    ];

    /// <summary>Compact "N kinds · …" text, or null when the module declares nothing (so the row can be omitted).</summary>
    public static string? Summary(IReadOnlyDictionary<string, int>? counts)
    {
        if (counts is null)
            return null;

        var parts = new List<string>();
        foreach (var (kind, singular, plural) in Known)
        {
            if (counts.TryGetValue(kind, out var n) && n > 0)
                parts.Add($"{n} {(n == 1 ? singular : plural)}");
        }

        foreach (var (kind, n) in counts
                     .Where(c => c.Value > 0 && !Known.Any(k => string.Equals(k.Kind, c.Key, StringComparison.Ordinal)))
                     .OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            parts.Add($"{n} {kind}");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
