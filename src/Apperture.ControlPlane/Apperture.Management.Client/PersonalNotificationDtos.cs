namespace Apperture.Management.Client;

public sealed class PersonalNotificationInboxItemDto
{
    public string Id { get; init; } = "";
    public string NotificationInstanceId { get; init; } = "";
    public bool IsRead { get; init; }
    public DateTimeOffset? ReadAtUtc { get; init; }
    public DateTimeOffset? FirstViewedAtUtc { get; init; }
    public DateTimeOffset? LastViewedAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Subject { get; init; } = "";
    public string Summary { get; init; } = "";
    public string NotificationKind { get; init; } = "";
    public string InstanceStatus { get; init; } = "";
    public string? IntegrationId { get; init; }
    public DateTimeOffset InstanceCreatedAtUtc { get; init; }
    public bool CanOpenAdminInstanceDetail { get; init; }
    public string? RenderedActionUrl { get; init; }
    public string? RenderedActionText { get; init; }
}

public sealed class PersonalNotificationUnreadCountDto
{
    public int UnreadCount { get; init; }
}

public sealed class PersonalNotificationMarkAllReadResultDto
{
    public int MarkedCount { get; init; }
}

public sealed class NotificationToastDeltaResponseDto
{
    public IReadOnlyList<NotificationToastDeltaItemDto> Items { get; init; } = [];
    public DateTimeOffset? NextAfterCreatedAtUtc { get; init; }
    public string? NextAfterId { get; init; }
    public int UnreadCount { get; init; }
}

public sealed class NotificationToastDeltaItemDto
{
    public string Id { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Summary { get; init; } = "";
    public string? Severity { get; init; }
    public string NotificationKind { get; init; } = "";
}
