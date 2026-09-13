using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Central registry of typed navigation-context definitions (UIX-4).
/// Keeps <c>from</c>/<c>fromKey</c> interpretation out of page markup.
/// </summary>
public static class ManagementNavigationContextCatalog
{
    private static readonly IManagementNavigationContextDefinition[] Definitions =
    [
        new DemoCustomerNavigationContextDefinition(),
    ];

    public static IReadOnlyList<string> SupportedTypes { get; } =
        Definitions.Select(d => d.Type).ToArray();

    public static bool TryResolve(
        string? type,
        string? key,
        out ManagementResolvedNavigationContext context)
    {
        context = default!;
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(key))
            return false;

        var typeTrimmed = type.Trim();
        var keyTrimmed = key.Trim();
        foreach (var definition in Definitions)
        {
            if (!string.Equals(definition.Type, typeTrimmed, StringComparison.Ordinal))
                continue;

            return definition.TryResolve(keyTrimmed, out context);
        }

        return false;
    }

    /// <summary>
    /// Parses <c>from</c>/<c>fromKey</c> from a navigation URI into a breadcrumb context seam.
    /// Incomplete, unsupported, or invalid values yield <see cref="ManagementBreadcrumbNavigationContext.None"/>.
    /// </summary>
    public static ManagementBreadcrumbNavigationContext TryParseBreadcrumbContext(string? navigationUri)
    {
        AffectedRecordNavigation.TryParseAffectedRecordNavigation(
            navigationUri,
            out _,
            out var returnContext);

        if (returnContext is null)
            return ManagementBreadcrumbNavigationContext.None;

        if (!TryResolve(returnContext.Type, returnContext.Key, out _))
            return ManagementBreadcrumbNavigationContext.None;

        return ManagementBreadcrumbNavigationContext.FromReturnContext(returnContext);
    }
}
