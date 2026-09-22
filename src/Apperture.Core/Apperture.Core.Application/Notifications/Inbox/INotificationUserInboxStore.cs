namespace Apperture.Core.Application.Notifications.Inbox;

public interface INotificationUserInboxStore
{
    ValueTask<IReadOnlyList<NotificationUserInboxItem>> ListForUserAsync(
        string principalUserId,
        PersonalNotificationListQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<int> CountUnreadAsync(string principalUserId, CancellationToken cancellationToken = default);

    ValueTask<bool> ExistsAsync(
        string principalUserId,
        string notificationInstanceId,
        CancellationToken cancellationToken = default);

    ValueTask AppendAsync(IReadOnlyList<NotificationUserInboxItemInsert> rows, CancellationToken cancellationToken = default);

    ValueTask<bool> MarkReadAsync(
        string principalUserId,
        string inboxItemId,
        DateTimeOffset readAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<bool> MarkUnreadAsync(
        string principalUserId,
        string inboxItemId,
        CancellationToken cancellationToken = default);

    ValueTask<int> MarkAllReadAsync(
        string principalUserId,
        DateTimeOffset readAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<int> TouchLastViewedAsync(
        string principalUserId,
        IReadOnlyList<string> inboxItemIds,
        DateTimeOffset viewedAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<int> TouchFirstViewedAsync(
        string principalUserId,
        IReadOnlyList<string> inboxItemIds,
        DateTimeOffset viewedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans this user's inbox in ascending (CreatedAtUtc, Id) order, starting strictly after the
    /// given composite cursor (or from the tip of the inbox when no cursor is given), joining in the
    /// definition's default severity/toast-eligibility and any explicit per-user Toast channel
    /// preference. Returns ALL scanned rows — toast-eligible or not — so the caller can advance the
    /// cursor past every row; eligibility filtering is the caller's responsibility, not this method's.
    /// <paramref name="afterCreatedAtUtc"/> and <paramref name="afterId"/> together form one cursor:
    /// pass both or neither (bootstrap).
    /// </summary>
    ValueTask<IReadOnlyList<NotificationToastDeltaRow>> ListToastDeltaAsync(
        string principalUserId,
        DateTimeOffset? afterCreatedAtUtc,
        string? afterId,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationUserInboxItemInsert(
    string Id,
    string NotificationInstanceId,
    string PrincipalUserId,
    string? DeliveryOutboxId,
    string SourceRecipientKind,
    string SourceRecipientRef,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// One scanned inbox row for the incremental Toast delta feed. <see cref="DefaultSeverity"/> is null
/// when the originating <c>NotificationDefinition</c> no longer exists (deleted definition); in that
/// case <see cref="IsToastEligible"/> is always false. Otherwise <see cref="IsToastEligible"/> is the
/// user's explicit Toast-channel preference if one exists, else the definition's
/// <c>ToastEnabledByDefault</c>.
/// </summary>
/// <remarks>
/// Deliberately narrow: this carries only what the Toast delta wire DTO needs. It previously also
/// carried <c>NotificationInstanceId</c>, <c>InstanceStatus</c>, and <c>IntegrationId</c>, but nothing
/// consumed them (no DTO, no test) so they were dropped rather than kept as untested dead weight. Re-add
/// them (and the corresponding projected columns in
/// <c>SqlNotificationUserInboxStore</c>) if a future phase needs them.
/// </remarks>
public sealed record NotificationToastDeltaRow(
    string Id,
    DateTimeOffset CreatedAtUtc,
    string RenderedSubject,
    string RenderedSummary,
    string NotificationKind,
    string? DefaultSeverity,
    bool IsToastEligible);
