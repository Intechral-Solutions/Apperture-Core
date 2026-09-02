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
    /// Gets a user by identity link (source + external id). Returns null when no link exists.
    /// </summary>
    ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default);

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
    /// </summary>
    ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of users with their identity links and role ids. For auth-admin listing.
    /// </summary>
    ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// The designated emergency-access user, if any. v1 allows at most one.
    /// </summary>
    ValueTask<User?> FindDesignatedEmergencyUserAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<User?>(null);

    /// <summary>
    /// Adds an identity link to an existing user. ExternalId for Local must be normalized (trim, lowercase) by the caller.
    /// Throws if (Source, ExternalId) already exists for any user.
    /// </summary>
    ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an identity link from a user. Throws if this is the user's last link.
    /// </summary>
    ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the owner and full record of a single identity link, or null when no link exists.
    /// </summary>
    async ValueTask<IdentityLinkOwner?> FindIdentityLinkAsync(
        IdentitySource source,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetByIdentityLinkAsync(source, externalId, cancellationToken);
        var link = user?.IdentityLinks.FirstOrDefault(l =>
            l.Source == source
            && string.Equals(l.ExternalId, externalId, StringComparison.OrdinalIgnoreCase));
        return user is null || link is null ? null : new IdentityLinkOwner(user.Id, link);
    }

    /// <summary>
    /// Adds an identity link including its provider metadata (tenant, provenance, snapshots).
    /// Throws if (Source, ExternalId) already exists for any user.
    /// </summary>
    ValueTask AddIdentityLinkAsync(UserId userId, IdentityLink link, CancellationToken cancellationToken = default) =>
        AddIdentityLinkAsync(userId, link.Source, link.ExternalId, cancellationToken);

    /// <summary>
    /// Marks an Entra link proven and completes metadata a legacy row may be missing. Tenant id is only
    /// written when the stored value is null, so an existing tenant binding is never silently changed.
    /// </summary>
    ValueTask CompleteEntraLinkProofAsync(
        string externalId,
        DateTimeOffset provenAt,
        string? tenantIdWhenMissing,
        string? registrationIdWhenMissing,
        string? providerSubject,
        CancellationToken cancellationToken = default) =>
        UpdateLastAuthenticatedAtAsync(IdentitySource.Entra, externalId, provenAt, cancellationToken);
}

/// <summary>
/// An identity link together with the platform user that owns it.
/// </summary>
public sealed record IdentityLinkOwner(UserId UserId, IdentityLink Link);
