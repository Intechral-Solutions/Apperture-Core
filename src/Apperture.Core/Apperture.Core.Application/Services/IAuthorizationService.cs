using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Evaluates whether an actor has permission on a resource. Integrates module-contributed permissions and break-glass path.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Evaluates whether the actor has the given permission, optionally scoped to a resource.
    /// </summary>
    ValueTask<bool> EvaluateAsync(
        IActor actor,
        PermissionDescriptor permission,
        ResourceDescriptor? resource,
        CancellationToken cancellationToken = default);
}
