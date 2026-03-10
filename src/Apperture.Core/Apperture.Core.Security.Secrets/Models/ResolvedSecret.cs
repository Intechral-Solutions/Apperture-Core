namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Runtime result of resolving a secret reference.
/// ToString is redacted to prevent accidental logging of secret material.
/// </summary>
public sealed record ResolvedSecret(
    SecretRef SecretRef,
    SecretMaterial Material,
    string? Version = null,
    DateTimeOffset? ExpiresAt = null,
    string? LeaseId = null)
{
    /// <summary>
    /// Redacted; does not include Material content to prevent accidental logging.
    /// </summary>
    public override string ToString() =>
        $"{nameof(ResolvedSecret)}(SecretRef={SecretRef}, MaterialType={Material.Type}, Version={Version})";
}
