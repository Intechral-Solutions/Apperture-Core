namespace Apperture.Core.Domain.Notifications;

public readonly record struct NotificationSubscriptionId(string Value)
{
    public static NotificationSubscriptionId New() => new($"nsub_{Guid.NewGuid():N}");

    public static NotificationSubscriptionId From(string value) => new(value);

    public override string ToString() => Value;
}
