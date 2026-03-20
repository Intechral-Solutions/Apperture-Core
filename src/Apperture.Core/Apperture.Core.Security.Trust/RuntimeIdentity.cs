namespace Apperture.Core.Security.Trust;

/// <summary>
/// Registered runtime identity (public key, cert thumbprint, or Managed Identity principal).
/// CredentialType should be a well-known value (e.g. "PublicKey", "CertificateThumbprint", "ManagedIdentity").
/// </summary>
public sealed record RuntimeIdentity(
    string Id,
    string DisplayName,
    string CredentialType,
    string? PublicKeyOrThumbprint,
    string? ManagedIdentityPrincipalId,
    IReadOnlyList<string> DeclaredScopes,
    DateTimeOffset RegisteredAt);

