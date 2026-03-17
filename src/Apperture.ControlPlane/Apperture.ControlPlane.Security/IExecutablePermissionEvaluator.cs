using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Evaluates definition-level required permissions for executable artifacts.
/// Semantics: if RequiredPermissions is non-empty, ALL listed permissions must pass.
/// Catalog coherence: if any required permission is unknown (not present in IResourceDescriptorRegistry), evaluation denies.
/// </summary>
public interface IExecutablePermissionEvaluator
{
    /// <summary>
    /// Evaluates all required permissions for the given actor.
    /// Returns false if any permission is denied, or if any permission is unknown in the registry.
    /// </summary>
    ValueTask<bool> EvaluateAllAsync(
        IActor actor,
        IReadOnlyList<PermissionDescriptor> requiredPermissions,
        CancellationToken cancellationToken = default);
}

