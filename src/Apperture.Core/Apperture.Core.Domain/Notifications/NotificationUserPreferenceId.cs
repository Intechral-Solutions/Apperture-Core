namespace Apperture.Core.Domain.Notifications;

public readonly record struct NotificationUserPreferenceId(string Value)
{
    public static NotificationUserPreferenceId New() => new($"nupref_{Guid.NewGuid():N}");

    public static NotificationUserPreferenceId From(string value) => new(value);

    public override string ToString() => Value;
}
