namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Runtime result of resolving a secret reference.
/// </summary>
public sealed record ResolvedSecret(
    SecretRef SecretRef,
    SecretMaterial Material,
    string? Version = null,
    DateTimeOffset? ExpiresAt = null,
    string? LeaseId = null);
