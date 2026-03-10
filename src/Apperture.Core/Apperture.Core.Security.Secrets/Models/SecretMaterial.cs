namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Secret payload container used only within provider/resolver boundaries.
/// ToString is redacted to prevent accidental logging of secret material.
/// </summary>
public sealed record SecretMaterial(
    SecretMaterialType Type,
    string? OpaqueString = null,
    string? Json = null,
    byte[]? Bytes = null,
    byte[]? X509Certificate = null)
{
    /// <summary>
    /// Redacted to prevent accidental logging of secret material.
    /// </summary>
    public override string ToString() => $"{nameof(SecretMaterial)}(Type={Type})";
}
