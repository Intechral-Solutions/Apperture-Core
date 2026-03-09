namespace Apperture.Bootstrap.Models;

/// <summary>
/// Non-secret SQL connection profile. No User/Password; credentials come via BootstrapCredentialReference.
/// </summary>
public sealed record SqlConnectionBootstrapProfile
{
    /// <summary>SQL Server host/instance.</summary>
    public required string Server { get; init; }

    /// <summary>Database name.</summary>
    public required string Database { get; init; }

    /// <summary>Authentication mode.</summary>
    public BootstrapAuthMode AuthMode { get; init; }

    /// <summary>When true, skips server certificate validation (dev only).</summary>
    public bool TrustServerCertificate { get; init; } = true;
}
