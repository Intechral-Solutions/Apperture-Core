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
    /// Creates the canonical Recovery Access account (<c>breakglass</c>) and designates it.
    /// When a designated emergency user already exists, returns that user id without creating another
    /// and without changing credentials. Username and display name are not caller-configurable.
    /// </summary>
    ValueTask<UserId> ProvisionAsync(
        string password,
        ActorId? actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes the designated emergency user in place to the canonical username and display name.
    /// Preserves UserId, credentials, TOTP, recovery codes, and designation. Throws
    /// <see cref="EmergencyAccessErrorCodes.UsernameConflict"/> without mutating when another user
    /// already owns the reserved username.
    /// </summary>
    ValueTask EnsureCanonicalIdentityAsync(CancellationToken cancellationToken = default);

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
