using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Core policy evaluation for authorization. Declares evaluation order per ADR-0012:
/// break-glass (reserved), relationship scopes (deferred), RBAC, resource ownership/scope.
/// Implementations delegate to IRoleProvider, IScopeContainmentEvaluator, and resource identity checks.
/// </summary>
public interface IAuthorizationPolicyEvaluator
{
    /// <summary>
    /// Evaluates whether the actor has the given permission.
    /// </summary>
    /// <param name="actor">The principal to evaluate.</param>
    /// <param name="permission">The required permission (action + optional resource descriptor).</param>
    /// <param name="resource">Optional resource descriptor for type/scope checks. Use when target is a governed resource.</param>
    /// <param name="resourceIdentity">Optional instance identity for ownership/scope checks. Use when evaluating against a specific governed entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        IGovernedResourceIdentity? resourceIdentity,
        CancellationToken cancellationToken = default);
}
