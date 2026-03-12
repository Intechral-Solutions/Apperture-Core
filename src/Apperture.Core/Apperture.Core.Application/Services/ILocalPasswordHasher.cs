namespace Apperture.Core.Application.Services;

/// <summary>
/// Hashes and verifies local user passwords. Do not use ISecretStore for this.
/// </summary>
public interface ILocalPasswordHasher
{
    /// <summary>
    /// Algorithm identifier stored with the hash (e.g. "PBKDF2-HMAC-SHA256").
    /// </summary>
    string AlgorithmName { get; }

    /// <summary>
    /// Produces a hash and salt for the given password. Caller stores both.
    /// </summary>
    (string Hash, string Salt) HashPassword(string plainPassword);

    /// <summary>
    /// Returns true if the plain password matches the stored hash when hashed with the given salt.
    /// </summary>
    bool VerifyPassword(string plainPassword, string storedHash, string storedSalt);
}
