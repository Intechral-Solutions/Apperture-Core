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
}
