namespace Apperture.Bootstrap.Models;

/// <summary>
/// Reference to where credentials live (Key Vault URI, cert thumbprint, etc.). No raw secret material.
/// Must not resolve from operational ISecretStore.
/// </summary>
public sealed record BootstrapCredentialReference
{
    /// <summary>Type of credential source.</summary>
    public BootstrapCredentialSourceType SourceType { get; init; }

    /// <summary>Reference (e.g. Key Vault secret URI, cert thumbprint). No raw secret.</summary>
    public string? Reference { get; init; }
}
