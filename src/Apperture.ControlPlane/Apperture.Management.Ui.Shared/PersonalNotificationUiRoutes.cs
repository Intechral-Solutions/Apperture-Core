namespace Apperture.Management.Ui.Shared;

using Apperture.Management.Client;

public static class PersonalNotificationUiRoutes
{
    public static string Inbox() => "/me/notifications";

    public static string AdminInstanceDetail(string instanceId) =>
        NotificationUiRoutes.InstanceDetail(instanceId);

    /// <summary>Prefer rendered operator action URL; fall back to admin instance detail when permitted.</summary>
    public static string? ResolveOpenTarget(PersonalNotificationInboxItemDto item, bool canOpenAdminInstanceDetail)
    {
        if (!string.IsNullOrWhiteSpace(item.RenderedActionUrl))
            return item.RenderedActionUrl.Trim();

        if (canOpenAdminInstanceDetail && item.CanOpenAdminInstanceDetail)
            return AdminInstanceDetail(item.NotificationInstanceId);

        return null;
    }
}
