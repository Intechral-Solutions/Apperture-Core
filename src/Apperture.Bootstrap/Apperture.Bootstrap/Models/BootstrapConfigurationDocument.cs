namespace Apperture.Bootstrap.Models;

/// <summary>
/// Persisted bootstrap configuration (non-secret profile + credential reference).
/// Contains no raw secrets.
/// </summary>
public sealed record BootstrapConfigurationDocument
{
    /// <summary>SQL connection profile (non-secret).</summary>
    public required SqlConnectionBootstrapProfile Profile { get; init; }

    /// <summary>Credential reference; no raw secret.</summary>
    public BootstrapCredentialReference? CredentialReference { get; init; }

    /// <summary>Version or ETag for optimistic concurrency.</summary>
    public string? Version { get; init; }
}
