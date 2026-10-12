namespace Apperture.Core.Application.Services;

public interface IManagedContactRepository
{
    Task<ManagedContactRepositoryRow?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the contact under an update lock held to the end of the caller's transaction (AUD-1.4 slice 6). Untracked, so a rerun
    /// reads afresh. Null when no such contact exists.
    /// </summary>
    Task<ManagedContactRepositoryRow?> GetByIdForUpdateAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the lock every writer of a contact's email address holds while it checks that no other contact has the address and
    /// writes it (an application lock owned by the caller's transaction), so the check cannot race another writer. Email
    /// uniqueness is an application rule, not an index. Must be called inside an open transaction; first among a contact writer's
    /// locks, after any integration or definition row it holds.
    /// </summary>
    /// <exception cref="InvalidOperationException">The lock was not granted in time.</exception>
    Task LockEmailNamespaceAsync(CancellationToken cancellationToken = default);

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
