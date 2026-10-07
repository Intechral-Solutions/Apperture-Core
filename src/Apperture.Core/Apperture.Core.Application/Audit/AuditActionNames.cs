namespace Apperture.Core.Application.Audit;

/// <summary>
/// Names of the actions written through the audit recorder. A converted writer names its action here and builds the event
/// through <see cref="AuditEvents"/>; the spelling is a persisted contract (every value is also declared in the catalog).
/// </summary>
public static class AuditActionNames
{
    // AUD-1.1
    public const string UsernameChanged = "governance.user.username.changed";
    public const string RecoveryAccessCanonicalized = "emergency.access.canonicalized";

    // AUD-1.2: roles, permissions, assignments, users, identity links and credentials
    public const string RoleCreated = "governance.role.created";
    public const string RoleMetadataUpdated = "governance.role.metadata.updated";
    public const string RoleDeleted = "governance.role.deleted";
    public const string PermissionAssignmentUpdated = "governance.permission.assignment.updated";
    public const string RoleMutationRejected = "governance.role.mutation_rejected";
    public const string UserCreated = "user.created";
    public const string RoleAssigned = "governance.role.assignment.created";
    public const string RoleRemoved = "governance.role.assignment.removed";
    public const string EntraLinkPending = "identity_link.entra.pending";
    public const string IdentityLinkAdded = "identity_link.added";
    public const string IdentityLinkRemoved = "identity_link.removed";
    public const string PasswordSet = "credential.password_set";
    public const string CredentialDisabled = "credential.disabled";
    public const string LocalCredentialReenabled = "credential.local.reenabled_strong_authentication";
    public const string BlockedLastAdminLinkRemoved = "auth_admin.blocked.last_admin_link_removed";
    public const string BlockedLastPlatformAdminRemoval = "auth_admin.blocked.last_platform_admin_removal";
    public const string BlockedLastAdminCredentialDisabled = "auth_admin.blocked.last_admin_credential_disabled";
    public const string StrongAuthenticationInsufficient = "auth.strong_authentication.insufficient";
    public const string UnsafeOperationBlocked = "emergency.auth.blocked.unsafe_operation";

    // AUD-1.2: Recovery Access administration
    public const string RecoveryAccessDesignated = "emergency.access.designated";
    public const string RecoveryAccessProvisioned = "emergency.access.provisioned";
    public const string RecoveryAccessRevoked = "emergency.access.revoked";

    // AUD-1.2: native MFA
    public const string MfaEnrolled = "auth.mfa.enrolled";
    public const string RecoveryCodesGenerated = "auth.mfa.recovery_codes.generated";
    public const string RecoveryCodeConsumed = "auth.mfa.recovery_code.consumed";
    public const string MfaReset = "auth.mfa.reset";
}
