using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub implementation. Returns null. To be replaced by environment-specific resolver.
/// </summary>
public sealed class StubBootstrapCredentialResolver : IBootstrapCredentialResolver
{
    public Task<string?> TryResolveAsync(BootstrapCredentialReference reference, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
