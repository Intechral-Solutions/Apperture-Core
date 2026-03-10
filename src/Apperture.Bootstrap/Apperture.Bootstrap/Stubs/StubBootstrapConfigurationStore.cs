using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub: GetAsync returns null (no persistence); SaveAsync throws. Replace with file- or DB-backed implementation for bootstrap config persistence.
/// Not for production.
/// </summary>
public sealed class StubBootstrapConfigurationStore : IBootstrapConfigurationStore
{
    /// <inheritdoc />
    public Task<BootstrapConfigurationDocument?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<BootstrapConfigurationDocument?>(null);

    /// <inheritdoc />
    public Task SaveAsync(BootstrapConfigurationDocument document, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Bootstrap configuration persistence is not implemented. Stub store cannot save. Use a file- or DB-backed IBootstrapConfigurationStore.");
}
