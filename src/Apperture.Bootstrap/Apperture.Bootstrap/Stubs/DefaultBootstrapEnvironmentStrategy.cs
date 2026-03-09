using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Default environment strategy. Returns minimal defaults; CreateResolver returns stub resolver.
/// Real implementations (Windows, Azure) go in Apperture.Bootstrap.Windows, Apperture.Bootstrap.Azure.
/// </summary>
public sealed class DefaultBootstrapEnvironmentStrategy : IBootstrapEnvironmentStrategy
{
    public IReadOnlyList<BootstrapCredentialSourceType> GetAvailableSourceTypes() =>
        [BootstrapCredentialSourceType.DpapiProtectedLocal];

    public BootstrapAuthMode GetDefaultAuthMode() => BootstrapAuthMode.Development;

    public IBootstrapCredentialResolver CreateResolver() => new StubBootstrapCredentialResolver();
}
