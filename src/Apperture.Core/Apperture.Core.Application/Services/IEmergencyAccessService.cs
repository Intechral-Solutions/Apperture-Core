using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Designates and inspects the instance emergency Platform Administrator. v1 allows exactly one.
/// </summary>
public interface IEmergencyAccessService
{
    ValueTask<EmergencyAccessPosture> GetPostureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Designates <paramref name="userId"/> as the emergency account. The user must already be a
    /// Platform Administrator with an enabled local credential. Replaces any previous designation.
    /// </summary>
    ValueTask DesignateAsync(UserId userId, ActorId? actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new local Platform Administrator and designates it as the emergency account.
    /// </summary>
    ValueTask<UserId> ProvisionAsync(
        string displayName,
        string username,
        string password,
        ActorId? actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears emergency designation. IAM-2 last-admin fallback becomes required again until a
    /// replacement is designated. Requires <paramref name="confirmFallback"/>.
    /// </summary>
    ValueTask RevokeAsync(bool confirmFallback, ActorId? actorId, CancellationToken cancellationToken = default);

    ValueTask SetLocalPasswordAsync(UserId userId, string password, ActorId? actorId, CancellationToken cancellationToken = default);

    ValueTask<bool> IsDesignatedEmergencyUserAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws when the operation would disable, un-role, or un-designate the healthy emergency account.
    /// </summary>
    ValueTask EnsureDestructiveChangeAllowedAsync(UserId userId, string operation, CancellationToken cancellationToken = default);
}
