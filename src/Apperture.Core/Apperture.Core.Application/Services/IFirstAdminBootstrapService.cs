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
    /// <param name="externalId">Stable external id for the link (e.g. Entra oid/sub, or local username). For Local, use normalized username.</param>
    /// <param name="password">Optional. When source is Local, if provided the local credential is created with this password (hashed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with UserId, or AlreadyExists, RoleNotFound, or DuplicateIdentityLink.</returns>
    Task<FirstAdminBootstrapResult> TryCreateFirstAdminAsync(
        string displayName,
        IdentitySource source,
        string externalId,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the platform has been initialized (at least one user exists).
    /// </summary>
    Task<BootstrapSetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default);
}
