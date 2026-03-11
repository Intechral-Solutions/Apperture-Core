using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Creates the first platform administrator when no users exist. First-admin bootstrap only; do not use for general user creation.
/// </summary>
public interface IFirstAdminBootstrapService
{
    /// <summary>
    /// Attempts to create the first platform user with one identity link and the platform administrator role.
    /// Succeeds only when zero platform users exist.
    /// </summary>
    /// <param name="displayName">Display name for the new user.</param>
    /// <param name="source">Identity source (Entra or Local) for the link.</param>
    /// <param name="externalId">Stable external id for the link (e.g. Entra oid/sub, or local username).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with UserId, or AlreadyExists, RoleNotFound, or DuplicateIdentityLink.</returns>
    Task<FirstAdminBootstrapResult> TryCreateFirstAdminAsync(
        string displayName,
        IdentitySource source,
        string externalId,
        CancellationToken cancellationToken = default);
}
