using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Persists and loads non-secret bootstrap configuration. Must not store raw secrets.
/// </summary>
public interface IBootstrapConfigurationStore
{
    /// <summary>Gets the current bootstrap configuration document.</summary>
    Task<BootstrapConfigurationDocument?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the bootstrap configuration document.</summary>
    Task SaveAsync(BootstrapConfigurationDocument document, CancellationToken cancellationToken = default);
}
