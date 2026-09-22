namespace Apperture.Core.Application.Notifications.Subscriptions;

public sealed record ResolvedNotificationRecipient(
    string Email,
    string? DisplayName,
    string? UserId,
    string? ManagedContactId,
    string Usability,
    string Source,
    string? SubscriptionId,
    string RecipientKind);

public sealed record SkippedNotificationRecipient(
    string? Email,
    string? DisplayName,
    string? UserId,
    string? ManagedContactId,
    string Source,
    string? SubscriptionId,
    string? RecipientKind,
    string Reason);

public sealed record InAppEligibleRecipient(
    string UserId,
    string? DisplayName,
    string? Email,
    string? ManagedContactId,
    string Source,
    string? SubscriptionId,
    string RecipientKind);

public sealed record NotificationRecipientResolutionPreview(
    IReadOnlyList<ResolvedNotificationRecipient> Deliverable,
    IReadOnlyList<SkippedNotificationRecipient> Skipped,
    IReadOnlyList<InAppEligibleRecipient> InApp);
