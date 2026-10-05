using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for platform users (human operator identity).
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets the total count of platform users. Used for first-admin bootstrap gate (succeeds only when zero).
    /// </summary>
    ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by id.
    /// </summary>
    ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by identity link. Local identities use source + external id.
    /// Entra callers should use <see cref="GetByIdentityLinkAsync(ProviderIdentityKey, CancellationToken)"/>
    /// so tenant id is part of the lookup. Passing <see cref="IdentitySource.Entra"/> here is rejected
    /// by the SQL repository.
    /// </summary>
    ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by canonical identity key. Entra lookups are tenant + object id.
    /// </summary>
    ValueTask<User?> GetByIdentityLinkAsync(ProviderIdentityKey identity, CancellationToken cancellationToken = default)
    {
        if (identity.Source == IdentitySource.Entra)
        {
            throw new ArgumentException(
                "Entra identity lookup requires a tenant-scoped query (Source + TenantId + ExternalId). Implement GetByIdentityLinkAsync(ProviderIdentityKey). Do not resolve by object id and filter tenant afterward.",
                nameof(identity));
        }

        return GetByIdentityLinkAsync(identity.Source, identity.ExternalId, cancellationToken);
    }

    /// <summary>
    /// Finds the user whose <b>verified</b> contact email matches. Never used to create or link identities.
    /// </summary>
    ValueTask<User?> FindByVerifiedContactEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<User?>(null);

    /// <summary>
    /// Adds a new user together with its username ledger row, in one transaction. The username must be
    /// canonical and policy-shaped, and a Local link (at most one) must use the username as its external
    /// id. Throws <see cref="UsernameException"/> with <see cref="UsernameErrorCodes.Taken"/> when any
    /// user holds or has ever held the name; nothing is persisted in that case.
    /// </summary>
    ValueTask AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing user (display name, break-glass eligibility, etc.).
    /// </summary>
    ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last authenticated timestamp for the identity link with the given source and external id.
    /// Local identities use this overload. Entra proof uses
    /// <see cref="UpdateLastAuthenticatedAtAsync(ProviderIdentityKey, DateTimeOffset, CancellationToken)"/>.
    /// </summary>
    ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last authenticated timestamp. Entra updates are tenant-scoped.
    /// </summary>
    ValueTask UpdateLastAuthenticatedAtAsync(ProviderIdentityKey identity, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (identity.Source == IdentitySource.Entra)
        {
            throw new ArgumentException(
                "Entra last-authenticated updates require a tenant-scoped repository implementation. Use ProviderIdentityKey.Entra(tenantId, objectId).",
                nameof(identity));
        }

        return UpdateLastAuthenticatedAtAsync(identity.Source, identity.ExternalId, at, cancellationToken);
    }

    /// <summary>
    /// Gets a page of users with their identity links and role ids. For auth-admin listing.
    /// Excludes the designated emergency account so ordinary Governance Users administration does
    /// not treat Recovery Access as a manageable user. TotalCount matches the filtered set.
    /// </summary>
    ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of users that match <paramref name="criteria"/>: a username prefix or a case-insensitive
    /// display-name substring (LIKE wildcards in the query are literal characters). An exact username match comes
    /// first, then <c>Username</c> ascending. Email, UPN, mail snapshots and the user id are never matched. Same
    /// Recovery Access exclusion and filtered <c>TotalCount</c> as <see cref="GetPageAsync"/>.
    /// </summary>
    /// <remarks>
    /// The default serves only the unfiltered list. A repository that can search overrides it.
    /// </remarks>
    ValueTask<(IReadOnlyList<User> Page, int TotalCount)> SearchPageAsync(
        UserSearchCriteria criteria,
        CancellationToken cancellationToken = default) =>
        criteria.Query is null
            ? GetPageAsync(criteria.Skip, criteria.Take, cancellationToken)
            : throw new NotSupportedException("This user repository does not support searching users.");

    /// <summary>
    /// Resolves <see cref="UserReferenceDto"/> values for the given ids in as few round trips as the store allows.
    /// Includes Recovery Access. Ids with no matching user are omitted.
    /// </summary>
    /// <remarks>The default loads each user; a store that can project the three fields in one query overrides it.</remarks>
    async ValueTask<IReadOnlyList<UserReferenceDto>> GetReferencesAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken = default)
    {
        var references = new List<UserReferenceDto>(userIds.Count);
        foreach (var userId in userIds)
        {
            var user = await GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (user is not null)
                references.Add(new UserReferenceDto(user.Id.Value, user.Username, user.DisplayName));
        }

        return references;
    }

    /// <summary>
    /// Moves a user from <paramref name="expectedCurrentUsername"/> to <paramref name="newUsername"/> in one
    /// transaction: retires the current ledger row with <paramref name="retiredReason"/>, reactivates the
    /// user's own retired row for the new name or inserts a new one, and updates <c>Users.Username</c> and
    /// the Local link's external id. Optionally sets the display name in the same transaction. Policy checks
    /// belong to the caller (<see cref="UsernameService"/>); this method enforces ownership only.
    /// Throws <see cref="UsernameException"/> with <see cref="UsernameErrorCodes.Taken"/> when another user
    /// holds or held the name, or <see cref="UsernameErrorCodes.ConcurrentChange"/> when the user's username
    /// is no longer <paramref name="expectedCurrentUsername"/>. Does not touch credentials, roles or sessions.
    /// </summary>
    ValueTask<UsernameChangeOutcome> ChangeUsernameAsync(
        UserId userId,
        string expectedCurrentUsername,
        string newUsername,
        string retiredReason,
        string? newDisplayName = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// The ledger row for a canonical username, current or retired, or null when no user has ever held it.
    /// </summary>
    ValueTask<UsernameLedgerEntry?> FindUsernameLedgerEntryAsync(
        string username,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<UsernameLedgerEntry?>(null);

    /// <summary>
    /// The designated emergency-access user, if any. v1 allows at most one.
    /// </summary>
    ValueTask<User?> FindDesignatedEmergencyUserAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<User?>(null);

    /// <summary>
    /// Adds an identity link to an existing user. ExternalId for Local must equal the user's username
    /// (otherwise <see cref="UsernameException"/> with <see cref="UsernameErrorCodes.Mismatch"/>), and a user
    /// has at most one Local link.
    /// Throws if a matching identity already exists for any user (Local: source + external id; Entra: source + tenant + oid).
    /// </summary>
    ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an identity link from a user. Throws if this is the user's last link.
    /// </summary>
    ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the owner and full record of a single identity link, or null when no link exists.
    /// Entra lookups must include tenant id.
    /// </summary>
    async ValueTask<IdentityLinkOwner?> FindIdentityLinkAsync(
        ProviderIdentityKey identity,
        CancellationToken cancellationToken = default)
    {
        var user = await GetByIdentityLinkAsync(identity, cancellationToken);
        var link = user?.IdentityLinks.FirstOrDefault(identity.Matches);
        return user is null || link is null ? null : new IdentityLinkOwner(user.Id, link);
    }

    /// <summary>
    /// Finds a local (or other non-Entra) identity link. Entra callers must use
    /// <see cref="FindIdentityLinkAsync(ProviderIdentityKey, CancellationToken)"/>.
    /// </summary>
    ValueTask<IdentityLinkOwner?> FindIdentityLinkAsync(
        IdentitySource source,
        string externalId,
        CancellationToken cancellationToken = default) =>
        FindIdentityLinkAsync(ProviderIdentityKey.FromSourceAndExternalId(source, externalId), cancellationToken);

    /// <summary>
    /// Adds an identity link including its provider metadata (tenant, provenance, snapshots).
    /// Throws if a matching identity already exists for any user.
    /// </summary>
    ValueTask AddIdentityLinkAsync(UserId userId, IdentityLink link, CancellationToken cancellationToken = default)
    {
        if (link.Source == IdentitySource.Entra)
        {
            throw new InvalidOperationException(
                "Entra identity links require tenant id. Implement AddIdentityLinkAsync(UserId, IdentityLink) so uniqueness is Source + TenantId + ExternalId.");
        }

        return AddIdentityLinkAsync(userId, link.Source, link.ExternalId, cancellationToken);
    }

    /// <summary>
    /// Marks an Entra link proven. Lookup is tenant + object id. Registration id is provenance only
    /// and is written when the stored value is null.
    /// </summary>
    ValueTask CompleteEntraLinkProofAsync(
        string tenantId,
        string objectId,
        DateTimeOffset provenAt,
        string? registrationIdWhenMissing,
        string? providerSubject,
        CancellationToken cancellationToken = default) =>
        UpdateLastAuthenticatedAtAsync(ProviderIdentityKey.Entra(tenantId, objectId), provenAt, cancellationToken);
}

/// <summary>
/// An identity link together with the platform user that owns it.
/// </summary>
public sealed record IdentityLinkOwner(UserId UserId, IdentityLink Link);

/// <summary>
/// One username ever assigned, permanently bound to <paramref name="UserId"/>. Null
/// <paramref name="RetiredAtUtc"/> means it is the user's current username.
/// </summary>
public sealed record UsernameLedgerEntry(
    string Username,
    UserId UserId,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset? RetiredAtUtc,
    string? RetiredReason)
{
    public bool IsCurrent => RetiredAtUtc is null;
}

/// <summary>What a username change did.</summary>
public enum UsernameChangeOutcome
{
    /// <summary>The user already had that username; only the display name may have changed.</summary>
    Unchanged,

    /// <summary>A name never held before was assigned.</summary>
    Assigned,

    /// <summary>One of the user's own retired names was reactivated.</summary>
    Reclaimed,
}
