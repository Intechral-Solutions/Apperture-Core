namespace Apperture.Core.Application.Services;

public interface IManagedContactRepository
{
    Task<ManagedContactRepositoryRow?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive email match among contacts that have a non-empty email.</summary>
    Task<ManagedContactRepositoryRow?> FindByEmailAsync(string emailAddress, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManagedContactRepositoryRow>> SearchAsync(string? query, int take, CancellationToken cancellationToken = default);

    Task<string> CreateCustomAsync(
        string displayName,
        string? emailAddress,
        bool isEnabled,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task UpdateContactAsync(
        string id,
        string displayName,
        string? emailAddress,
        bool isEnabled,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task SetLinkedUserIdAsync(string managedContactId, string? userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task SetIsEnabledAsync(string id, bool isEnabled, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a managed contact from Entra directory data; never creates a platform user.
    /// Stable dedupe by Entra object id via identity link.
    /// </summary>
    Task<UpsertEntraManagedContactResult> UpsertFromEntraDirectoryUserAsync(
        string displayName,
        string? mail,
        string? userPrincipalName,
        string entraObjectId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<ManagedContactIdentityLinkRepositoryRow?> FindIdentityLinkByEntraObjectIdAsync(
        string entraObjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManagedContactIdentityLinkRepositoryRow>> ListLinksForContactAsync(
        string managedContactId,
        CancellationToken cancellationToken = default);
}

public sealed record ManagedContactRepositoryRow(
    string Id,
    string DisplayName,
    string? EmailAddress,
    string Source,
    bool IsEnabled,
    string? LinkedUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ManagedContactIdentityLinkRepositoryRow(
    string Id,
    string ManagedContactId,
    string Provider,
    string ExternalObjectId,
    string? ExternalPrincipalName,
    DateTimeOffset? LastSyncedAtUtc,
    string LinkStatus,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpsertEntraManagedContactResult(
    string ManagedContactId,
    bool Created,
    ManagedContactIdentityLinkRepositoryRow Link);
