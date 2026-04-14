using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Connector packages implement this to register <see cref="IConnectionVerifier"/> (and any verifier dependencies such as client factories)
/// during <c>AddApperture…Connector(IServiceCollection)</c> composition. Do not invoke from hosted services after the host is built.
/// </summary>
public interface IConnectionVerifierContributor
{
    /// <summary>
    /// Registers scoped (or singleton) <see cref="IConnectionVerifier"/> implementations for this connector definition id(s).
    /// </summary>
    void Register(IServiceCollection services);
}
