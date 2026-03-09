using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Default environment strategy. Returns both Windows and Azure source types; CreateResolver returns stub resolver.
/// Real implementations (Windows, Azure) go in Apperture.Bootstrap.Windows, Apperture.Bootstrap.Azure.
/// Azure hosts should register their own IBootstrapEnvironmentStrategy.
/// </summary>
public sealed class DefaultBootstrapEnvironmentStrategy : IBootstrapEnvironmentStrategy
{
    public IReadOnlyList<BootstrapCredentialSourceType> GetAvailableSourceTypes() =>
    [
        BootstrapCredentialSourceType.ManagedIdentity,
        BootstrapCredentialSourceType.WindowsIntegrated,
        BootstrapCredentialSourceType.KeyVaultReference,
        BootstrapCredentialSourceType.DpapiProtectedLocal,
        BootstrapCredentialSourceType.CertificateBackedLocal,
        BootstrapCredentialSourceType.DeploymentInjected
    ];

    public BootstrapAuthMode GetDefaultAuthMode() => BootstrapAuthMode.Development;

    public IBootstrapCredentialResolver CreateResolver() => new StubBootstrapCredentialResolver();
}
