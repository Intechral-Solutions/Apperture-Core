using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Evaluates whether an actor has permission on a resource. Integrates module-contributed permissions and break-glass path.
/// Per ADR-0012.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Evaluates whether the actor has the given permission, optionally scoped to a resource.
    /// </summary>
    /// <param name="actor">The principal to evaluate.</param>
    /// <param name="permission">The required permission (action + optional resource descriptor).</param>
    /// <param name="resource">Optional resource descriptor for type/scope checks. When null, performs type-level permission check only.</param>
    /// <param name="resourceIdentity">Optional instance identity for ownership/scope checks. When the target is a governed resource, pass resource.GetIdentity() to enable instance-level validation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para><b>When resource is null:</b> Type-level permission check only. Use for actions that do not target a specific resource instance.</para>
    /// <para><b>When the target is a governed resource:</b> Caller should pass resource derived from governedResource.GetIdentity()
    /// as ResourceDescriptor(identity.ResourceType, identity.ScopeKey ?? "instance"). Use GovernedResourceDescriptor.FromIdentity for convenience.
    /// Also pass resourceIdentity = governedResource.GetIdentity() to enable ownership and instance-level checks.</para>
    /// <para>Evaluation uses RBAC (roles/permissions) and scope containment per ADR-0012.</para>
    /// </remarks>
    ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        IGovernedResourceIdentity? resourceIdentity = null,
        CancellationToken cancellationToken = default);
}
