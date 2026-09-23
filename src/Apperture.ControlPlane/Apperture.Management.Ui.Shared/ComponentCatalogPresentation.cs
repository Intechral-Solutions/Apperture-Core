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

    /// <summary>The Component detail a Connectors-tab row links to.</summary>
    public static string ConnectorDetailHref(string connectorDefinitionId) =>
        DetailHref(ConnectorKind, connectorDefinitionId);

    public const string ModuleHasNoComponentsMessage = "This module provides no catalog components.";

    /// <summary>
    /// Empty copy for the module's Components section. A caller who can read only one Component family cannot
    /// conclude the module provides none, so the wording is scoped to what they can see.
    /// </summary>
    public static string ModuleComponentsEmptyMessage(bool canViewAllFamilies) =>
        canViewAllFamilies ? ModuleHasNoComponentsMessage : "This module provides no catalog components you can view.";

    /// <summary>The existing Connection page a usage row opens.</summary>
    public static string ConnectionHref(string connectionId) =>
        $"/connections/{Uri.EscapeDataString(connectionId)}";

    /// <summary>Usage means Active Connections; the count is complete even when fewer rows are named.</summary>
    public static string ConnectorUsageSummary(int activeCount, int namedCount) =>
        activeCount <= 0 ? "No active connections use this connector yet."
        : activeCount == 1 ? "1 active connection uses this connector."
        : namedCount < activeCount
            ? $"{activeCount} active connections use this connector. Showing the first {namedCount}."
            : $"{activeCount} active connections use this connector.";

    /// <summary>Current subscriptions only. Historical activity is not usage and is never counted or mentioned.</summary>
    public static string SubscriptionUsageSummary(int subscriptionCount) =>
        subscriptionCount <= 0 ? "No current subscriptions."
        : subscriptionCount == 1 ? "1 current subscription."
        : $"{subscriptionCount} current subscriptions.";

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
