namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Request model for creating a secret descriptor and initial version metadata.
/// </summary>
public sealed record SecretCreateRequest(
    string Name,
    string Provider,
    SecretMaterialType MaterialType,
    string? InitialVersion = null,
    IReadOnlyDictionary<string, string>? Tags = null);
