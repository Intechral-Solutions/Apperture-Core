namespace Apperture.Core.Domain.Notifications;

public static class NotificationInstanceStatuses
{
    public const string Pending = "pending";
    public const string Suppressed = "suppressed";
}

public static class NotificationOutboxStatuses
{
    public const string Pending = "pending";
    public const string Delivering = "delivering";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Suppressed = "suppressed";
    public const string Skipped = "skipped";
}

public static class NotificationChannels
{
    public const string Email = "email";
    public const string Toast = "toast";
}
