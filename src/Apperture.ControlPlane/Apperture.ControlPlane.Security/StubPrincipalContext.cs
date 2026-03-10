using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Phase 1 stub: returns no current actor. Replaced in Phase 2 when Entra/local identity is wired.
/// </summary>
public sealed class StubPrincipalContext : IPrincipalContext
{
    /// <inheritdoc />
    public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IActor?>(null);
}
