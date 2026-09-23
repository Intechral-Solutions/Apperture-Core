using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Pure presentation rules for the Component catalog: readable labels, routes, filter normalization and
/// list wording. A Component's native id stays authoritative; labels here only make it legible.
/// </summary>
public static class ComponentCatalogPresentation
{
    // These mirror the server's closed token sets. They are used only to accept or reject a filter value; the
    // options an operator is offered come from the authorized items the server actually returned.
    private const string ConnectorKind = "connector";
    private const string NotificationDefinitionKind = "notification-definition";
    private const string SystemOrigin = "system";
    private const string ModuleOrigin = "module";

    public const string EmptyMessage = "No components are available to you.";
    public const string NoMatchMessage = "No components match the current search or filters.";

    public static string KindLabel(string kind) => kind switch
    {
        ConnectorKind => "Connector",
        NotificationDefinitionKind => "Notification definition",
        _ => kind
    };

    public static string OriginLabel(string origin) => origin switch
    {
        SystemOrigin => "System",
        ModuleOrigin => "Module",
        _ => origin
    };

    /// <summary>Platform-supplied Components have no module; the list never resolves module display names.</summary>
    public static string SuppliedBy(string? supplyingModuleId) =>
        string.IsNullOrWhiteSpace(supplyingModuleId) ? "Platform" : supplyingModuleId;

    public static string DetailHref(string kind, string nativeId) =>
        $"/components/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(nativeId)}";

    public static string CreateConnectionHref(string connectorDefinitionId) =>
        $"/connections/new?connectorDefinitionId={Uri.EscapeDataString(connectorDefinitionId)}";

    /// <summary>The authoritative management surface for a Notification Definition; Component detail only links here.</summary>
    public static string NotificationDefinitionHref(string definitionId) =>
        $"/config/notifications/definitions/{Uri.EscapeDataString(definitionId)}";

    public static string? NormalizeKind(string? requested, IReadOnlyList<string> availableKinds) =>
        string.IsNullOrWhiteSpace(requested) || !availableKinds.Contains(requested, StringComparer.Ordinal)
            ? null
            : requested;

    public static string? NormalizeOrigin(string? requested) =>
        requested is SystemOrigin or ModuleOrigin ? requested : null;

    public static string? NormalizeModule(string? requested, IReadOnlyList<ComponentModuleRefDto> availableModules) =>
        string.IsNullOrWhiteSpace(requested)
        || !availableModules.Any(m => string.Equals(m.ModuleId, requested, StringComparison.Ordinal))
            ? null
            : requested;

    public static bool IsFiltered(string? kind, string? origin, string? moduleId, string? query) =>
        !string.IsNullOrWhiteSpace(kind)
        || !string.IsNullOrWhiteSpace(origin)
        || !string.IsNullOrWhiteSpace(moduleId)
        || !string.IsNullOrWhiteSpace(query);

    public static string ResultsSummary(int shown, int totalMatching) =>
        shown <= 0 ? ""
        : shown < totalMatching ? $"Showing {shown} of {totalMatching} components"
        : shown == 1 ? "1 component"
        : $"{shown} components";
}
