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
    /// Adds a new user.
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
    /// Updates display name and the Local identity ExternalId in one SaveChanges. Does not recreate
    /// the user, local credential, TOTP, or recovery-code rows. Throws when another user already
    /// owns <paramref name="newLocalUsername"/>.
    /// </summary>
    ValueTask ApplyCanonicalLocalIdentityAsync(
        UserId userId,
        string displayName,
        string currentLocalUsername,
        string newLocalUsername,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// The designated emergency-access user, if any. v1 allows at most one.
    /// </summary>
    ValueTask<User?> FindDesignatedEmergencyUserAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<User?>(null);

    /// <summary>
    /// Adds an identity link to an existing user. ExternalId for Local must be normalized (trim, lowercase) by the caller.
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
