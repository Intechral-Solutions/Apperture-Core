using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Phase 1 stub: deny-by-default. Replaced in Phase 2 with real role + permission evaluation.
/// </summary>
public sealed class StubAuthorizationService : IAuthorizationService
{
    /// <inheritdoc />
    public ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}
