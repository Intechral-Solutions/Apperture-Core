using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for local (username/password) credentials. One credential per user for local auth.
/// Do not use ISecretStore for user passwords.
/// </summary>
public interface ILocalCredentialRepository
{
    /// <summary>
    /// Gets the local credential for a user, if any.
    /// </summary>
    ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or updates the password hash and salt for a user. Creates the row if it does not exist.
    /// </summary>
    ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the disabled flag for a user's local credential.
    /// </summary>
    ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only view of a stored local credential (for verification only; no plain password).
/// </summary>
public sealed record LocalCredentialData(UserId UserId, string PasswordHash, string Salt, string Algorithm, bool IsDisabled);
