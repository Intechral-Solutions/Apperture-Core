namespace Apperture.Bootstrap.Models;

/// <summary>
/// SQL Server authentication mode for bootstrap connection.
/// Must not conflate with operational secret store.
/// </summary>
public enum BootstrapAuthMode
{
    /// <summary>Azure Managed Identity.</summary>
    ManagedIdentity,

    /// <summary>Windows Integrated Security.</summary>
    WindowsIntegrated,

    /// <summary>SQL user/password authentication.</summary>
    SqlPassword,

    /// <summary>Certificate-based authentication.</summary>
    CertificateBased,

    /// <summary>Development/local only (e.g. LocalDB).</summary>
    Development
}
