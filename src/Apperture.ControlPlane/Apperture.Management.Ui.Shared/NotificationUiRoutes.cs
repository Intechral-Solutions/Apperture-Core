namespace Apperture.Management.Ui.Shared;

public static class NotificationUiRoutes
{
    public static string DefinitionDetail(string definitionId) =>
        $"/config/notifications/definitions/{Uri.EscapeDataString(definitionId)}";

    public static string DefinitionInstances(string definitionId) =>
        $"/config/notifications/instances?definitionId={Uri.EscapeDataString(definitionId)}";

    public static string InstanceDetail(string instanceId) =>
        $"/config/notifications/instances/{Uri.EscapeDataString(instanceId)}";

    public static string IntegrationNotifications(string integrationId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}/notifications";

    public static string IntegrationScheduleDiagnostics(string integrationId, string? attentionIncidentId = null)
    {
        var path = $"/integrations/{Uri.EscapeDataString(integrationId)}/schedule-diagnostics";
        if (string.IsNullOrWhiteSpace(attentionIncidentId))
            return path;
        return $"{path}?incidentId={Uri.EscapeDataString(attentionIncidentId.Trim())}";
    }

    public static string DeliveryDiagnostics() => "/config/notifications/diagnostics";

    public static string Landing() => "/config/notifications";

    public static string DefinitionsList() => "/config/notifications/definitions";

    public static string DefinitionCreate() => "/config/notifications/definitions/new";

    public static string DefinitionEdit(string definitionId) =>
        $"/config/notifications/definitions/{Uri.EscapeDataString(definitionId)}/edit";

    public static string InstancesList() => "/config/notifications/instances";

    public static string Delivery() => "/config/notifications/delivery";

    public static string IntegrationDefinitionCreate(string integrationId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}/notifications/definitions/new";

    public static string DiagnosticsForIntegration(string integrationId) =>
        $"{DeliveryDiagnostics()}?integrationId={Uri.EscapeDataString(integrationId)}";

    public static string DiagnosticsForDefinition(string definitionId) =>
        $"{DeliveryDiagnostics()}?definitionId={Uri.EscapeDataString(definitionId)}";

    public static string IntegrationCutoverReadiness(string integrationId) =>
        $"{IntegrationNotifications(integrationId)}#cutover-readiness";
}
