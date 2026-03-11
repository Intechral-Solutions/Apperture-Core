using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stub implementation of IAuthorizationPolicyEvaluator. Always denies.
/// Phase 2 scaffolding; replace with RBAC-based implementation in Phase 3.
/// </summary>
public sealed class StubAuthorizationPolicyEvaluator : IAuthorizationPolicyEvaluator
{
    /// <inheritdoc />
    public ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        IGovernedResourceIdentity? resourceIdentity,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}
