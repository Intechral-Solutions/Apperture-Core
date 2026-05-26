namespace Apperture.Management.Ui.Shared;

public static class IntegrationUiRoutes
{
    public static string Detail(string integrationId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}";

    public static string Settings(string integrationId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}/settings";

    public static string Notifications(string integrationId) =>
        NotificationUiRoutes.IntegrationNotifications(integrationId);

    public static string ScheduleDiagnostics(string integrationId) =>
        NotificationUiRoutes.IntegrationScheduleDiagnostics(integrationId);
}
