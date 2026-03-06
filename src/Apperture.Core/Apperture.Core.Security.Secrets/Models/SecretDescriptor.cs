namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Metadata for a logical secret and its currently active version.
/// </summary>
public sealed record SecretDescriptor(
    SecretId SecretId,
    string Name,
    string Provider,
    SecretMaterialType MaterialType,
    string? ActiveVersion,
    SecretState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null);
