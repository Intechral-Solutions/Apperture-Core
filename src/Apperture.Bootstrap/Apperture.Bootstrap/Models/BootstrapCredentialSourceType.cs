namespace Apperture.Bootstrap.Models;

/// <summary>
/// Source of bootstrap SQL credentials. Must not resolve from operational ISecretStore.
/// </summary>
public enum BootstrapCredentialSourceType
{
    /// <summary>Azure Managed Identity.</summary>
    ManagedIdentity,

    /// <summary>Windows Integrated Security.</summary>
    WindowsIntegrated,

    /// <summary>Azure Key Vault reference (secret URI).</summary>
    KeyVaultReference,

    /// <summary>DPAPI-protected local bootstrap credential.</summary>
    DpapiProtectedLocal,

    /// <summary>Certificate-backed local reference.</summary>
    CertificateBackedLocal,

    /// <summary>Secure deployment-injected secret reference.</summary>
    DeploymentInjected
}
