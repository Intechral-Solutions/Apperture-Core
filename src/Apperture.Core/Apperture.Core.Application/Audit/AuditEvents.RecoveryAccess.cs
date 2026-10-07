using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Audit;

// AUD-1.2: Recovery Access administration (the emergency command line). The actor is the command line, or the platform
// when the host applies a change itself; the target is the Recovery Access user. No password, hash or code is recorded.
public static partial class AuditEvents
{
    /// <summary>A user was designated as the Recovery Access account, replacing <paramref name="previous"/> if any.</summary>
    public static AuditEvent RecoveryAccessDesignated(UserId user, UserId? previous) =>
        Event(
            AuditActionNames.RecoveryAccessDesignated,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("previousUserId", previous?.Value));

    /// <summary>The Recovery Access account was created and designated.</summary>
    public static AuditEvent RecoveryAccessProvisioned(UserId user, string username) =>
        Event(
            AuditActionNames.RecoveryAccessProvisioned,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("username", username));

    /// <summary>The Recovery Access designation was removed; the installation returns to last-administrator protection.</summary>
    public static AuditEvent RecoveryAccessRevoked(UserId user) =>
        Event(
            AuditActionNames.RecoveryAccessRevoked,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value));
}
