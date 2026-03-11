using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stub implementation of IPrincipalToUserResolver. Always returns null.
/// Used when identity is not wired; policy evaluator will deny Human actors (no roles).
/// </summary>
public sealed class StubPrincipalToUserResolver : IPrincipalToUserResolver
{
    /// <inheritdoc />
    public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<UserId?>(null);
}
