namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Type hint for secret payload representation.
/// </summary>
public enum SecretMaterialType
{
    OpaqueString = 0,
    Json = 1,
    Bytes = 2,
    X509Certificate = 3
}
