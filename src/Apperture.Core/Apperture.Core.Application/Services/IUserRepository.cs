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
}
