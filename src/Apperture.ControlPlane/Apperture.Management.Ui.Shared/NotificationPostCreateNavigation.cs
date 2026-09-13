namespace Apperture.Management.Ui.Shared;

/// <summary>Query parameters and URLs for post-create notification definition flows (Epic 77 phase 8B).</summary>
public static class NotificationPostCreateNavigation
{
    public const string CreatedDefinitionIdQuery = "createdDefinitionId";
    public const string IntegrationIdQuery = "integrationId";

    public static string IntegrationNotificationsAfterCreate(string integrationId, string definitionId) =>
        $"/integrations/{Uri.EscapeDataString(integrationId)}/notifications?{CreatedDefinitionIdQuery}={Uri.EscapeDataString(definitionId)}";

    public static string DefinitionDetailAfterCreate(string definitionId, string? integrationId)
    {
        var path = $"/config/notifications/definitions/{Uri.EscapeDataString(definitionId)}?{CreatedDefinitionIdQuery}={Uri.EscapeDataString(definitionId)}";
        if (!string.IsNullOrWhiteSpace(integrationId))
            path += $"&{IntegrationIdQuery}={Uri.EscapeDataString(integrationId.Trim())}";
        return path;
    }
}
