namespace Apperture.Core.Domain.Notifications;

public readonly record struct NotificationDefinitionId(string Value)
{
    public static NotificationDefinitionId New() => new($"ndef_{Guid.NewGuid():N}");

    public static NotificationDefinitionId From(string value) => new(value);

    public override string ToString() => Value;
}
