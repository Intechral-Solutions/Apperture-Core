namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Secret payload container used only within provider/resolver boundaries.
/// </summary>
public sealed record SecretMaterial(
    SecretMaterialType Type,
    string? OpaqueString = null,
    string? Json = null,
    byte[]? Bytes = null,
    byte[]? X509Certificate = null);
