using System.Security.Cryptography;
using Apperture.Core.Application.Services;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hasher for local auth. Do not use for runtime secrets.
/// </summary>
public sealed class LocalPasswordHasher : ILocalPasswordHasher
{
    public const string AlgorithmValue = "PBKDF2-HMAC-SHA256";
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int IterationCount = 100_000;

    public string AlgorithmName => AlgorithmValue;

    public (string Hash, string Salt) HashPassword(string plainPassword)
    {
        var salt = new byte[SaltSizeBytes];
        RandomNumberGenerator.Fill(salt);
        var hash = KeyDerivation.Pbkdf2(
            plainPassword,
            salt,
            KeyDerivationPrf.HMACSHA256,
            IterationCount,
            HashSizeBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public bool VerifyPassword(string plainPassword, string storedHash, string storedSalt)
    {
        var salt = Convert.FromBase64String(storedSalt);
        var hash = KeyDerivation.Pbkdf2(
            plainPassword,
            salt,
            KeyDerivationPrf.HMACSHA256,
            IterationCount,
            HashSizeBytes);
        var computed = Convert.ToBase64String(hash);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(storedHash),
            hash);
    }
}
