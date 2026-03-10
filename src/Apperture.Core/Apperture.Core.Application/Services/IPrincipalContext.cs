using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Resolves the current principal with optional roles, permissions, and relationship context.
/// Used by authorization and audit. May aggregate IIdentityResolutionService and IRoleProvider.
/// </summary>
public interface IPrincipalContext
{
    /// <summary>
    /// Resolves the current actor (principal) from the ambient auth context.
    /// </summary>
    ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default);
}
