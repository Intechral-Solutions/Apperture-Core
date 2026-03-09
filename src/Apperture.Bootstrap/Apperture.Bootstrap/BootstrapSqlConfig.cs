using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Bootstrap SQL Server configuration. For production, use <see cref="CredentialReference"/> with
/// <see cref="IBootstrapCredentialResolver"/>. Plaintext User/Password are allowed only in Development.
/// </summary>
public sealed class BootstrapSqlConfig
{
    public const string SectionName = "SqlServer";

    /// <summary>
    /// SQL Server host/instance (e.g. localhost, (localdb)\mssqllocaldb).
    /// </summary>
    public string? Server { get; set; }

    /// <summary>
    /// Database name. Defaults to Apperture if not set.
    /// </summary>
    public string? Database { get; set; }

    /// <summary>
    /// SQL authentication user. DEV ONLY: when set with Password, uses SQL auth.
    /// In Production, use <see cref="CredentialReference"/> instead.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// SQL authentication password. DEV ONLY: when set with User, uses SQL auth.
    /// In Production, use <see cref="CredentialReference"/> instead.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// When true, skips server certificate validation (dev only).
    /// </summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>
    /// Reference to credential source (Key Vault URI, cert thumbprint, etc.). Preferred for production.
    /// Resolved via <see cref="IBootstrapCredentialResolver"/>; no plaintext in config.
    /// </summary>
    public BootstrapCredentialReference? CredentialReference { get; set; }
}
