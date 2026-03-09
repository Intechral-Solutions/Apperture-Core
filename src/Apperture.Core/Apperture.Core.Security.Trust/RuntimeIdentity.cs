namespace Apperture.Core.Security.Trust;

/// <summary>
/// Registered runtime identity (public key, cert thumbprint, or Managed Identity principal).
/// </summary>
public sealed record RuntimeIdentity(
    string Id,
    string DisplayName,
    string CredentialType,
    string? PublicKeyOrThumbprint,
    DateTimeOffset RegisteredAt);
