using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Audit;

// AUD-1.2: the typed producers of first-administrator bootstrap and bootstrap recovery. Request text, passwords and recovery
// proof are never recorded.
public static partial class AuditEvents
{
    /// <summary>The target id of rows about the instance as a whole.</summary>
    public const string InstanceTargetId = "instance";

    /// <summary>The first platform administrator was created by the anonymous bootstrap request.</summary>
    public static AuditEvent FirstAdminCreated(UserId user, string username, bool localSignInReady) =>
        Event(AuditActionNames.FirstAdminCreated, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("username", username), ("localSignInReady", localSignInReady));

    /// <summary>A first-administrator request was refused because the instance already has users.</summary>
    public static AuditEvent FirstAdminRejectedAlreadyInitialized() =>
        Event(AuditActionNames.FirstAdminRejectedAlreadyInitialized, Target(AuditTargetTypes.Instance, InstanceTargetId), AuditOutcomeCodes.Rejected);

    /// <summary>
    /// A first-administrator request was refused as invalid. <paramref name="reason"/> is <c>missing_fields</c>,
    /// <c>invalid_identity_source</c>, <c>password_policy</c> or <c>username_policy</c>.
    /// </summary>
    public static AuditEvent FirstAdminRejectedInvalidRequest(string reason) =>
        Event(AuditActionNames.FirstAdminRejectedInvalidRequest, Target(AuditTargetTypes.Instance, InstanceTargetId), AuditOutcomeCodes.Rejected,
            ("reason", reason));

    /// <summary>Bootstrap recovery gave a user the platform administrator role.</summary>
    public static AuditEvent RestorePlatformAccessExecuted(UserId user, RoleId role) =>
        Event(AuditActionNames.RestorePlatformAccessExecuted, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("roleId", role.Value));

    /// <summary>Bootstrap recovery was not authorized.</summary>
    public static AuditEvent RestorePlatformAccessDenied() =>
        Event(AuditActionNames.RestorePlatformAccessDenied, Target(AuditTargetTypes.Instance, InstanceTargetId), AuditOutcomeCodes.Denied,
            ("reason", "not_authorized"));

    /// <summary>Bootstrap recovery found no user to restore. The requested identifier is not recorded.</summary>
    public static AuditEvent RestorePlatformAccessTargetNotFound() =>
        Event(AuditActionNames.RestorePlatformAccessFailed, Target(AuditTargetTypes.Instance, InstanceTargetId), AuditOutcomeCodes.Failed,
            ("reason", "target_not_found"));
}
