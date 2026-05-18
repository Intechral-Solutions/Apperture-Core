namespace Apperture.Core.Domain.Notifications;

public readonly record struct NotificationInstanceId(string Value)
{
    public static NotificationInstanceId New() => new($"ninst_{Guid.NewGuid():N}");

    public static NotificationInstanceId From(string value) => new(value);

    public override string ToString() => Value;
}

public readonly record struct NotificationDeliveryOutboxId(string Value)
{
    public static NotificationDeliveryOutboxId New() => new($"nout_{Guid.NewGuid():N}");

    public static NotificationDeliveryOutboxId From(string value) => new(value);

    public override string ToString() => Value;
}
