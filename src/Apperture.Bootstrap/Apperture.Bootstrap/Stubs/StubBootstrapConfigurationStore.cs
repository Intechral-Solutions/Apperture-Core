using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub implementation. Throws NotImplementedException. To be replaced by file- or DB-backed implementation.
/// </summary>
public sealed class StubBootstrapConfigurationStore : IBootstrapConfigurationStore
{
    public Task<BootstrapConfigurationDocument?> GetAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("IBootstrapConfigurationStore is not implemented. Use file- or DB-backed implementation.");

    public Task SaveAsync(BootstrapConfigurationDocument document, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("IBootstrapConfigurationStore is not implemented. Use file- or DB-backed implementation.");
}
