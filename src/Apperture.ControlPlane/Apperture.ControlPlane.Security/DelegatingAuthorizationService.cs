using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// IAuthorizationService implementation that delegates to IAuthorizationPolicyEvaluator.
/// Caller passes resource; resourceIdentity is derived as null (scope checks use resource descriptor).
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
        CancellationToken cancellationToken = default) =>
        _policyEvaluator.EvaluateAsync(actor, permission, resource, resourceIdentity: null, cancellationToken);
}
