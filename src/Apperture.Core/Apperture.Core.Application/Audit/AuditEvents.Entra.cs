using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Audit;

// AUD-1.2: the typed producers of provisioning, access requests, Entra association and the Entra-primary local-credential
// policy. A Microsoft identity is recorded as its tenant id and object id only; a refused identity is targeted by its
// canonical external key (entra/{tenantId}/{objectId}), never by email, UPN or display name.
public static partial class AuditEvents
{
    /// <summary>A user was added from the Microsoft directory with a pending (unproven) link.</summary>
    public static AuditEvent ProvisionedFromDirectory(UserId user, string username, string tenantId, string objectId, string registrationId) =>
        Event(AuditActionNames.ProvisionedFromDirectory, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("username", username), ("tenantId", tenantId), ("objectId", objectId), ("registrationId", registrationId));

    /// <summary>A user was added by approving an access request, with the link the request proved.</summary>
    public static AuditEvent ProvisionedFromAccessRequest(UserId user, string username, string tenantId, string objectId, string requestId) =>
        Event(AuditActionNames.ProvisionedFromAccessRequest, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("username", username), ("tenantId", tenantId), ("objectId", objectId), ("requestId", requestId));

    /// <summary>A validated Microsoft identity with no Apperture user asked for access.</summary>
    public static AuditEvent AccessRequestCreated(string requestId, string registrationId, string tenantId, string objectId) =>
        Event(AuditActionNames.AccessRequestCreated, Target(AuditTargetTypes.EntraAccessRequest, requestId), AuditOutcomeCodes.Requested,
            ("requestId", requestId), ("registrationId", registrationId), ("tenantId", tenantId), ("objectId", objectId));

    /// <summary>An administrator approved an access request; <paramref name="user"/> is the user it created.</summary>
    public static AuditEvent AccessRequestApproved(string requestId, string tenantId, string objectId, UserId user) =>
        Event(AuditActionNames.AccessRequestApproved, Target(AuditTargetTypes.EntraAccessRequest, requestId), AuditOutcomeCodes.Succeeded,
            ("requestId", requestId), ("tenantId", tenantId), ("objectId", objectId), ("userId", user.Value));

    /// <summary>An administrator rejected an access request. This is a decision, not a refused attempt.</summary>
    public static AuditEvent AccessRequestRejected(string requestId, string tenantId, string objectId) =>
        Event(AuditActionNames.AccessRequestRejected, Target(AuditTargetTypes.EntraAccessRequest, requestId), AuditOutcomeCodes.Succeeded,
            ("requestId", requestId), ("tenantId", tenantId), ("objectId", objectId), ("userId", null));

    /// <summary>A pending access request was closed because its identity already belongs to <paramref name="user"/>.</summary>
    public static AuditEvent AccessRequestReconciled(string requestId, string tenantId, string objectId, UserId user) =>
        Event(AuditActionNames.AccessRequestReconciled, Target(AuditTargetTypes.EntraAccessRequest, requestId), AuditOutcomeCodes.Succeeded,
            ("requestId", requestId), ("tenantId", tenantId), ("objectId", objectId), ("userId", user.Value));

    /// <summary>
    /// The welcome mail of a provisioned user was handed to the transport after the user committed: sent when the transport
    /// accepted it; failed (<see cref="AuditOutcomeCodes.Failed"/>, or <see cref="AuditOutcomeCodes.Unknown"/> when the send
    /// threw) otherwise. The address and the transport's text are never recorded.
    /// </summary>
    public static AuditEvent Welcome(UserId user, string outcome, string? failure) =>
        Event(
            outcome == AuditOutcomeCodes.Succeeded ? AuditActionNames.WelcomeSent : AuditActionNames.WelcomeFailed,
            Target(AuditTargetTypes.User, user.Value),
            outcome,
            ("userId", user.Value),
            ("failure", failure));

    /// <summary>A pending Microsoft link was proven by its user's own sign-in.</summary>
    public static AuditEvent EntraLinkProven(UserId user, string linkId, string tenantId, string objectId) =>
        Event(AuditActionNames.EntraLinkProven, Target(AuditTargetTypes.IdentityLink, linkId), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("linkId", linkId), ("tenantId", tenantId), ("objectId", objectId));

    /// <summary>A user linked a Microsoft identity to their own account by signing in with it.</summary>
    public static AuditEvent EntraLinked(UserId user, string linkId, string tenantId, string objectId) =>
        Event(AuditActionNames.EntraLinked, Target(AuditTargetTypes.IdentityLink, linkId), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("linkId", linkId), ("tenantId", tenantId), ("objectId", objectId));

    /// <summary>
    /// A user's attempt to link a Microsoft identity was refused. <paramref name="reason"/> is <c>emergency_account</c> or
    /// <c>user_already_has_entra_link</c> (link_failed), or <c>owned_by_other_user</c> or <c>concurrent_insert</c> (collision).
    /// </summary>
    public static AuditEvent EntraLinkRefused(UserId user, string tenantId, string objectId, string reason) =>
        Event(
            reason is "owned_by_other_user" or "concurrent_insert" ? AuditActionNames.EntraLinkCollision : AuditActionNames.EntraLinkFailed,
            ExternalTarget(tenantId, objectId),
            AuditOutcomeCodes.Rejected,
            ("userId", user.Value),
            ("tenantId", tenantId),
            ("objectId", objectId),
            ("reason", reason));

    /// <summary>
    /// A validated Microsoft identity from a directory other than the active registration's was refused, at sign-in or while
    /// <paramref name="linkingUser"/> tried to link it.
    /// </summary>
    public static AuditEvent EntraTenantMismatch(string tenantId, string objectId, UserId? linkingUser) =>
        Event(AuditActionNames.EntraTenantMismatch, ExternalTarget(tenantId, objectId), AuditOutcomeCodes.Rejected,
            ("tenantId", tenantId), ("objectId", objectId), ("flow", linkingUser is null ? "sign_in" : "link"), ("userId", linkingUser?.Value));

    /// <summary>A validated Microsoft identity linked to no Apperture user tried to sign in.</summary>
    public static AuditEvent EntraSignInRejectedUnlinked(string tenantId, string objectId) =>
        Event(AuditActionNames.EntraSignInRejectedUnlinked, ExternalTarget(tenantId, objectId), AuditOutcomeCodes.Rejected,
            ("tenantId", tenantId), ("objectId", objectId));

    /// <summary>A user's local password sign-in was disabled because their Microsoft link became proven.</summary>
    public static AuditEvent LocalDisabledEntraPrimary(UserId user, bool recoveryAccessHealthy) =>
        Event(AuditActionNames.LocalDisabledEntraPrimary, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Succeeded,
            ("userId", user.Value), ("recoveryAccessHealthy", recoveryAccessHealthy));

    /// <summary>The Entra-primary policy left the Recovery Access account's local sign-in enabled, as it always does.</summary>
    public static AuditEvent RecoveryAccessLocalProtected(UserId user) =>
        Event(AuditActionNames.RecoveryAccessLocalProtected, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Blocked,
            ("userId", user.Value));

    /// <summary>The Entra-primary policy deferred disabling the last Entra-independent platform administrator's local sign-in.</summary>
    public static AuditEvent LocalDisableDeferredLastAdmin(UserId user) =>
        Event(AuditActionNames.LocalDisableDeferredLastAdmin, Target(AuditTargetTypes.User, user.Value), AuditOutcomeCodes.Blocked,
            ("userId", user.Value));

    private static AuditTarget ExternalTarget(string tenantId, string objectId) =>
        Target(AuditTargetTypes.ExternalIdentity, new AuditExternalActor(AuditExternalActor.EntraProvider, tenantId, objectId).CanonicalKey);
}
