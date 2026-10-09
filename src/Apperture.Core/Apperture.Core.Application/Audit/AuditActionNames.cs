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

    // AUD-1.2: account services (invitations, contact email, password setup and reset, profile, own notification preferences)
    public const string InvitationSent = "account.invitation.sent";
    public const string InvitationResent = "account.invitation.resent";
    public const string InvitationAccepted = "account.invitation.accepted";
    public const string PasswordSetupCompleted = "credential.password.setup_completed";
    public const string EmailVerificationRequested = "account.email.verification_requested";
    public const string EmailChangeRequested = "account.email.change_requested";
    public const string EmailVerified = "account.email.verified";
    public const string EmailChanged = "account.email.changed";
    public const string PasswordResetRequested = "credential.password.reset_requested";
    public const string PasswordResetRequestSuppressed = "credential.password.reset_request.suppressed";
    public const string PasswordResetCompleted = "credential.password.reset_completed";
    public const string SecurityMailDeliveryAttempted = "account.security_mail.delivery_attempted";
    public const string DisplayNameUpdated = "account.profile.display_name.updated";
    public const string AvatarReset = "account.profile.avatar.reset";
    public const string AvatarUpdated = "account.profile.avatar.updated";
    public const string AvatarUploadRejected = "account.profile.avatar.upload_rejected";
    public const string NotificationEmailOptedIn = "account.notification.email.opted_in";
    public const string NotificationEmailOptedOut = "account.notification.email.opted_out";
    public const string NotificationToastOptedIn = "account.notification.toast.opted_in";
    public const string NotificationToastOptedOut = "account.notification.toast.opted_out";

    // AUD-1.2: provisioning and access requests
    public const string ProvisionedFromDirectory = "user.provisioned.entra_directory";
    public const string ProvisionedFromAccessRequest = "user.provisioned.entra_access_request";
    public const string AccessRequestCreated = "entra.access_request.created";
    public const string AccessRequestApproved = "entra.access_request.approved";
    public const string AccessRequestRejected = "entra.access_request.rejected";
    public const string AccessRequestReconciled = "entra.access_request.reconciled_by_provisioning";
    public const string WelcomeSent = "user.provisioning.welcome.sent";
    public const string WelcomeFailed = "user.provisioning.welcome.failed";

    // AUD-1.2: Entra association and the Entra-primary local-credential policy
    public const string EntraLinkProven = "identity_link.entra.proven";
    public const string EntraLinked = "identity_link.entra.linked";
    public const string EntraLinkFailed = "identity_link.entra.link_failed";
    public const string EntraLinkCollision = "identity_link.entra.collision";
    public const string EntraTenantMismatch = "identity_link.entra.tenant_mismatch";
    public const string EntraSignInRejectedUnlinked = "auth.entra.login.rejected_unlinked";
    public const string LocalDisabledEntraPrimary = "credential.local.disabled_entra_primary";
    public const string RecoveryAccessLocalProtected = "emergency.local.protected";
    public const string LocalDisableDeferredLastAdmin = "credential.local.disable_deferred_last_entra_independent_admin";

    // AUD-1.2: authentication (successes are W2; attempts, refusals and sign-out are W3)
    public const string LocalSignInSucceeded = "auth.local.login.success";
    public const string RecoveryAccessSignInSucceeded = "auth.emergency.login.success";
    public const string BreakGlassSignInSucceeded = "auth.break_glass.login.success";
    public const string EntraSignInSucceeded = "auth.entra.login.success";
    public const string EntraSignInFailed = "auth.entra.login.failed";
    public const string SignedOut = "auth.sign_out";
    public const string LocalPasswordChanged = "auth.local.password_change.success";
    public const string LocalPasswordChangeRefused = "auth.local.password_change.failed";
    public const string StepUpInitiated = "auth.strong_authentication.step_up.initiated";
    public const string StepUpFailed = "auth.strong_authentication.step_up.failed";
    public const string StepUpTenantMismatch = "auth.strong_authentication.step_up.tenant_mismatch";
    public const string StepUpIdentityMismatch = "auth.strong_authentication.step_up.identity_mismatch";
    public const string StepUpContextNotSatisfied = "auth.strong_authentication.step_up.authentication_context_not_satisfied";
    public const string StepUpSatisfied = "auth.strong_authentication.step_up.satisfied";

    // AUD-1.2: Entra tenant registrations, the access-request policy and the sign-in policy
    public const string TenantRegistrationCreated = "entra.tenant_registration.created";
    public const string TenantRegistrationUpdated = "entra.tenant_registration.updated";
    public const string TenantRegistrationDeleted = "entra.tenant_registration.deleted";
    public const string TenantRegistrationActivated = "entra.tenant_registration.activated";
    public const string TenantRegistrationChangeRefused = "entra.tenant_registration.change_refused";
    public const string AccessRequestPolicyChanged = "entra.access_request.policy.changed";
    public const string SignInPolicyUpdated = "auth.sign_in_policy.updated";

    // AUD-1.2: first-administrator bootstrap and bootstrap recovery
    public const string FirstAdminCreated = "bootstrap.first_admin.created";
    public const string ConfiguredAdminCreated = "bootstrap.configured_admin.created";
    public const string FirstAdminRejectedAlreadyInitialized = "bootstrap.first_admin.rejected_already_initialized";
    public const string FirstAdminRejectedInvalidRequest = "bootstrap.first_admin.rejected_invalid_request";
    public const string RestorePlatformAccessExecuted = "recovery.restore_platform_access.executed";
    public const string RestorePlatformAccessDenied = "recovery.restore_platform_access.denied";
    public const string RestorePlatformAccessFailed = "recovery.restore_platform_access.failed";

    // AUD-1.2: runtime enrollment
    public const string RuntimeEnrollmentInvitationCreated = "governance.runtime.enrollment.invitation.created";
    public const string RuntimeEnrollmentCompleted = "governance.runtime.enrollment.completed";

    // AUD-1.3: operator actions on runs (W1 requests; the refusals beside them are W3)
    public const string WorkflowStartRequested = "execution.workflow_start.requested";
    public const string OperationStartRequested = "execution.operation_start.requested";
    public const string WorkflowStartBlocked = "execution.workflow_start.blocked";
    public const string OperationStartBlocked = "execution.operation_start.blocked";
    public const string WorkflowRunCancelRequested = "execution.workflow_run.cancel_requested";
    public const string OperationRunCancelRequested = "execution.operation_run.cancel_requested";
    public const string WorkflowRunParkRequested = "execution.workflow_run.park_requested";
    public const string WorkflowRunResumeRequested = "execution.workflow_run.resume_requested";
    public const string WorkflowRetryRequested = "execution.workflow_retry.requested";
    public const string WorkflowRetryBlocked = "execution.workflow_retry.blocked";
    public const string OperationItemRetryRequested = "execution.operation_item.retry_requested";
    public const string OperationItemRetryBlocked = "execution.operation_item.retry_blocked";
    public const string OperationItemAttentionAcknowledged = "execution.operation_item.attention.acknowledged";
    public const string OperationItemAttentionReopened = "execution.operation_item.attention.reopened";

    // AUD-1.3: recovery and the manual scheduler operations
    public const string BlockedWorkRetryRequested = "recovery.blocked_work.retry_requested";
    public const string BlockedWorkRetryRefused = "recovery.blocked_work.retry_refused";
    public const string RecoveryRetryDenied = "recovery.retry.denied";
    public const string SchedulerOperationDenied = "recovery.run_cycle.denied";
    public const string SchedulerOperationRequested = "recovery.schedules.run_cycle_requested";

    // AUD-1.4: secret-store events, recorded under the Secrets category on the configuration entity that owns the secret.
    public const string TenantRegistrationClientSecretStored = "entra.tenant_registration.client_secret.stored";
    public const string TenantRegistrationClientSecretRetired = "entra.tenant_registration.client_secret.retired";

    // AUD-1.4: platform and integration settings. The set and reset actions were declared by AUD-1.0 and are converted here; the
    // secret actions are the Secrets-category rows of the same units, recorded on the setting that owns the secret.
    public const string PlatformSettingSet = "platform.settings.set";
    public const string PlatformSettingReset = "platform.settings.reset";
    public const string IntegrationSettingSet = "integration.settings.set";
    public const string IntegrationSettingReset = "integration.settings.reset";
    public const string PlatformSettingSecretStored = "platform.settings.secret.stored";
    public const string PlatformSettingSecretRetired = "platform.settings.secret.retired";
    public const string IntegrationSettingSecretStored = "integration.settings.secret.stored";
    public const string IntegrationSettingSecretRetired = "integration.settings.secret.retired";

    // AUD-1.4 slice 3: schedule hard-delete. Both were declared by AUD-1.0 as legacy-bridge entries and are converted here.
    public const string ScheduleDeleted = "schedule.deleted";
    public const string ScheduleDeleteRejected = "schedule.delete_rejected";

    // AUD-1.4 slice 3, work package 4a: integration hard-delete. Declared by AUD-1.0 as a legacy-bridge entry, converted here.
    public const string IntegrationDeleted = "integration.deleted";
}
