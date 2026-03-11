using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// IAuthorizationService implementation that delegates to IAuthorizationPolicyEvaluator.
/// Passes resource and resourceIdentity through for scope and ownership checks.
/// </summary>
public sealed class DelegatingAuthorizationService : IAuthorizationService
{
    private readonly IAuthorizationPolicyEvaluator _policyEvaluator;

    public DelegatingAuthorizationService(IAuthorizationPolicyEvaluator policyEvaluator)
    {
        _policyEvaluator = policyEvaluator;
    }

    /// <inheritdoc />
    public ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        IGovernedResourceIdentity? resourceIdentity = null,
        CancellationToken cancellationToken = default) =>
        _policyEvaluator.EvaluateAsync(actor, permission, resource, resourceIdentity, cancellationToken);
}
