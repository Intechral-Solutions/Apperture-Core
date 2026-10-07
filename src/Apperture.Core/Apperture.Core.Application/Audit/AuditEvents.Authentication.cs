using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Audit;

// AUD-1.2: the typed producers of authentication. A sign-in success is W2: its row commits before the session is issued. No
// password, code, token, claim or provider text is ever recorded.
public static partial class AuditEvents
{
    /// <summary>
    /// A local sign-in completed. The action follows the account: the Recovery Access account's sign-in is
    /// <c>auth.emergency.login.success</c>. <paramref name="mechanism"/> is <c>password</c>, <c>native_totp</c> or
    /// <c>native_recovery_code</c>.
    /// </summary>
    public static AuditEvent LocalSignInSucceeded(UserId user, bool recoveryAccess, string mechanism) =>
        Event(
            recoveryAccess ? AuditActionNames.RecoveryAccessSignInSucceeded : AuditActionNames.LocalSignInSucceeded,
            Target(AuditTargetTypes.User, user.Value),
            AuditOutcomeCodes.Succeeded,
            ("userId", user.Value),
            ("mechanism", mechanism));

    /// <summary>A break-glass sign-in completed (legacy endpoint, disabled by default).</summary>
    public static AuditEvent BreakGlassSignInSucceeded(UserId user) =>
        Event(AuditActionNames.BreakGlassSignInSucceeded, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("mechanism", "password"));

    /// <summary>A Microsoft sign-in resolved to a user and a session is about to be issued.</summary>
    public static AuditEvent EntraSignInSucceeded(UserId user, string tenantId, string objectId) =>
        Event(AuditActionNames.EntraSignInSucceeded, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("tenantId", tenantId), ("objectId", objectId));

    /// <summary>
    /// A Microsoft round trip ended without a validated identity. <paramref name="flow"/> is <c>sign_in</c>, <c>link</c>,
    /// <c>step_up</c> or <c>unknown</c> (the state did not validate); <paramref name="cancelled"/> when the provider answered
    /// <c>access_denied</c>.
    /// </summary>
    public static AuditEvent EntraSignInFailed(string flow, bool cancelled) =>
        Event(AuditActionNames.EntraSignInFailed, null, cancelled ? AuditOutcomeCodes.Denied : AuditOutcomeCodes.Failed,
            ("flow", flow), ("reason", cancelled ? "cancelled" : "failed"));

    /// <summary>A signed-in user ended their session. <paramref name="session"/> is <c>local</c> or <c>entra</c>.</summary>
    public static AuditEvent SignedOut(UserId user, string session) =>
        Event(AuditActionNames.SignedOut, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("session", session));

    /// <summary>A user changed their own local password. The password, its hash and its salt are never recorded.</summary>
    public static AuditEvent LocalPasswordChanged(UserId user) =>
        Event(AuditActionNames.LocalPasswordChanged, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value));

    /// <summary>A self-service password change was refused; <paramref name="reason"/> is the stable refusal reason.</summary>
    public static AuditEvent LocalPasswordChangeRefused(string userId, string reason) =>
        Event(AuditActionNames.LocalPasswordChangeRefused, Target(AuditTargetTypes.User, userId), AuditOutcomeCodes.Rejected,
            ("userId", userId), ("reason", reason));

    /// <summary>A signed-in user was sent to Microsoft for strong authentication.</summary>
    public static AuditEvent StepUpInitiated(UserId user, string requestedContext, string session) =>
        Event(AuditActionNames.StepUpInitiated, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Requested,
            ("userId", user.Value), ("requestedContext", requestedContext), ("session", session));

    /// <summary>A step-up was refused before or after the identity check; <paramref name="reason"/> is the stable reason.</summary>
    public static AuditEvent StepUpFailed(UserId user, string reason, string? tenantId = null, string? objectId = null) =>
        Event(AuditActionNames.StepUpFailed, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Failed,
            ("userId", user.Value), ("reason", reason), ("tenantId", tenantId), ("objectId", objectId));

    /// <summary>A step-up returned an identity from a directory other than the active registration's.</summary>
    public static AuditEvent StepUpTenantMismatch(UserId user, string tenantId, string objectId) =>
        Event(AuditActionNames.StepUpTenantMismatch, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Rejected,
            ("userId", user.Value), ("tenantId", tenantId), ("objectId", objectId), ("reason", null), ("otherUserId", null));

    /// <summary>A step-up returned an identity that is not the initiating user's own link.</summary>
    public static AuditEvent StepUpIdentityMismatch(UserId user, string tenantId, string objectId, UserId? otherUser) =>
        Event(AuditActionNames.StepUpIdentityMismatch, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Rejected,
            ("userId", user.Value), ("tenantId", tenantId), ("objectId", objectId),
            ("reason", otherUser is null ? "no_link" : "different_user"), ("otherUserId", otherUser?.Value));

    /// <summary>A step-up did not satisfy the requested authentication context.</summary>
    public static AuditEvent StepUpContextNotSatisfied(UserId user, string requestedContext) =>
        Event(AuditActionNames.StepUpContextNotSatisfied, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Failed,
            ("userId", user.Value), ("requestedContext", requestedContext));

    /// <summary>A step-up satisfied the requested authentication context; the stronger session is about to be issued.</summary>
    public static AuditEvent StepUpSatisfied(UserId user, string requestedContext, string tenantId, string objectId, bool developmentSimulated) =>
        Event(AuditActionNames.StepUpSatisfied, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("requestedContext", requestedContext), ("tenantId", tenantId), ("objectId", objectId),
            ("developmentSimulated", developmentSimulated));
}
