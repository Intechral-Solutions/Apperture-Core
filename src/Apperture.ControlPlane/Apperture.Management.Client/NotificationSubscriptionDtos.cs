namespace Apperture.Management.Client;

public sealed record NotificationSubscriptionDto(
    string Id,
    string NotificationDefinitionId,
    string? IntegrationId,
    string RecipientKind,
    string RecipientRef,
    string DeliveryMode,
    string Origin,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AddNotificationSubscriptionRequest(
    string? IntegrationId,
    string? RecipientKind,
    string? RecipientRef,
    string? ManagedContactInput,
    bool CreateManagedContactIfMissing = false,
    string DeliveryMode = "immediate");

public sealed record PatchNotificationSubscriptionRequest(
    bool? IsEnabled,
    string? DeliveryMode);

public sealed record ResolveNotificationRecipientsRequest(string? IntegrationId);

public sealed record ResolvedNotificationRecipientDto(
    string Email,
    string? DisplayName,
    string? UserId,
    string? ManagedContactId,
    string Usability,
    string Source,
    string? SubscriptionId,
    string RecipientKind);

public sealed record SkippedNotificationRecipientDto(
    string? Email,
    string? DisplayName,
    string? UserId,
    string? ManagedContactId,
    string Source,
    string? SubscriptionId,
    string? RecipientKind,
    string Reason);

public sealed record NotificationRecipientResolutionPreviewDto(
    IReadOnlyList<ResolvedNotificationRecipientDto> Deliverable,
    IReadOnlyList<SkippedNotificationRecipientDto> Skipped);

public sealed record NotificationUserOptOutRequestDto(
    string UserId,
    string? OperationalTeamId);

public sealed record NotificationSubscriptionUserOverrideDto(
    string Id,
    string UserId,
    string NotificationDefinitionId,
    string? OperationalTeamId,
    DateTimeOffset CreatedAtUtc);
