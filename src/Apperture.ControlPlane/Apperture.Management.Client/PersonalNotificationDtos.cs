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

    /// <summary>
    /// Unread count from the cheap, no-repair store scan (<c>CountUnreadAsync</c>), NOT the
    /// repair/backfill-wrapped count that <c>GET api/me/notifications/unread-count</c> returns
    /// (<c>GetUnreadCountAsync</c>, which can materialize additional inbox rows before counting). For a
    /// user with un-materialized backlog, this value can be genuinely lower than that endpoint's --
    /// repair is deliberately never run on this ~30-second polled path. A future badge-liveness consumer
    /// (the P5 watcher) must pick ONE of these two sources for the visible unread badge and not mix them
    /// across different parts of the UI, or the badge will visibly flicker between two different numbers.
    /// </summary>
    public int UnreadCount { get; init; }

    /// <summary>
    /// True when the request that produced this response carried no cursor (<c>afterCreatedAtUtc</c> was
    /// null), meaning <see cref="Items"/> is the current "tip" of the inbox rather than an incremental
    /// delta since a prior poll. Computed by the controller from what the caller sent, not by the store.
    /// A watcher can use this to distinguish "first poll / bootstrap" from "nothing new since last poll"
    /// without having to separately track whether it sent a cursor on this call.
    /// </summary>
    public bool IsBootstrap { get; init; }
}

public sealed class NotificationToastDeltaItemDto
{
    public string Id { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Summary { get; init; } = "";
    public string? Severity { get; init; }
    public string NotificationKind { get; init; } = "";
}
