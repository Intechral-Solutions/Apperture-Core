namespace Apperture.Management.Ui.Shared;

public static class PersonalNotificationUiRoutes
{
    public static string Inbox() => "/me/notifications";

    public static string AdminInstanceDetail(string instanceId) =>
        NotificationUiRoutes.InstanceDetail(instanceId);
}
