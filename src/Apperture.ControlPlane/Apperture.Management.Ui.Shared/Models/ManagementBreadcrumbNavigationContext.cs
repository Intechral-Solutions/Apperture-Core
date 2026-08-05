namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// Optional, URL-derived navigation context for drill-through breadcrumb enrichment (UIX-4).
/// Carries validated typed identity only (<c>from</c> + <c>fromKey</c>); never labels or routes.
/// Parsed via <c>ManagementNavigationContextCatalog</c> from the current navigation URI.
/// </summary>
public sealed record ManagementBreadcrumbNavigationContext(string? Type = null, string? Key = null)
{
    /// <summary>Empty context — identical to calling resolve without context.</summary>
    public static ManagementBreadcrumbNavigationContext None { get; } = new();

    public bool HasValue =>
        !string.IsNullOrWhiteSpace(Type) && !string.IsNullOrWhiteSpace(Key);

    public static ManagementBreadcrumbNavigationContext FromReturnContext(
        ManagementNavigationReturnContext? context) =>
        context is null
            ? None
            : new ManagementBreadcrumbNavigationContext(context.Type, context.Key);

    public ManagementNavigationReturnContext? ToReturnContext() =>
        HasValue
            ? new ManagementNavigationReturnContext(Type!.Trim(), Key!.Trim())
            : null;
}
