namespace Apperture.Core.Application.Services;

public interface IManagedContactManagementService
{
    ValueTask<IReadOnlyList<ManagedContactSummary>> ListAsync(string? query, int take, CancellationToken cancellationToken = default);

    ValueTask<ManagedContactDetail?> GetDetailAsync(string id, CancellationToken cancellationToken = default);

    ValueTask<ManagedContactDetail> CreateCustomAsync(ManagedContactCreateCommand command, CancellationToken cancellationToken = default);

    ValueTask<ManagedContactDetail?> UpdateCustomAsync(string id, ManagedContactUpdateCommand command, CancellationToken cancellationToken = default);

    ValueTask<ManagedContactDetail?> SetEnabledAsync(string id, bool isEnabled, CancellationToken cancellationToken = default);

    ValueTask<ImportEntraUsersResult> ImportEntraUsersAsync(
        string entraRegistrationId,
        IReadOnlyList<string> entraUserObjectIds,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ManagedContactGroupSummary>> ImportEntraGroupsAsync(
        string entraRegistrationId,
        IReadOnlyList<string> entraGroupObjectIds,
        CancellationToken cancellationToken = default);

    ValueTask<ManagedContactDetail?> LinkToUserAsync(string managedContactId, string userId, CancellationToken cancellationToken = default);

    ValueTask<ManagedContactDetail?> UnlinkUserAsync(string managedContactId, CancellationToken cancellationToken = default);
}

public sealed record ManagedContactCreateCommand(string DisplayName, string? EmailAddress, bool IsEnabled);

public sealed record ManagedContactUpdateCommand(string DisplayName, string? EmailAddress, bool IsEnabled);

public sealed record ManagedContactSummary(
    string Id,
    string DisplayName,
    string? EmailAddress,
    string Source,
    bool IsEnabled,
    string UserLinkStatus,
    string NotificationUsabilityStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record ManagedContactIdentityLinkDetail(
    string Id,
    string Provider,
    string ExternalObjectId,
    string? ExternalPrincipalName,
    DateTimeOffset? LastSyncedAtUtc,
    string LinkStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record ManagedContactDetail(
    string Id,
    string DisplayName,
    string? EmailAddress,
    string Source,
    bool IsEnabled,
    string? LinkedUserId,
    string UserLinkStatus,
    string NotificationUsabilityStatus,
    IReadOnlyList<ManagedContactIdentityLinkDetail> IdentityLinks,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ImportEntraUserItemResult(
    string EntraObjectId,
    bool Success,
    string? ManagedContactId,
    string OutcomeCode,
    string? ErrorMessage);

public sealed record ImportEntraUsersResult(IReadOnlyList<ImportEntraUserItemResult> Items);

public sealed record ManagedContactGroupSummary(
    string Id,
    string DisplayName,
    string EntraGroupObjectId,
    DateTimeOffset UpdatedAtUtc);
