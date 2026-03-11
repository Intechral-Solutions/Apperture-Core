using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Resolves a Human IActor to UserId for role lookup. IRoleProvider is user-centric.
/// Returns null for Machine, System, RemoteInstance, or unlinked Human actors.
/// </summary>
public interface IPrincipalToUserResolver
{
    /// <summary>
    /// Resolves the actor to a UserId when the actor is Human and linked to a User.
    /// </summary>
    ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default);
}
