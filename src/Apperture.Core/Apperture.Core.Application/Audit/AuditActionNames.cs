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
    public const string RoleAssignmentRejected = "governance.role.assignment_rejected";
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

    // AUD-1.4 slice 4: integration, binding and schedule lifecycle. New actions; the writers they describe had no recorder row.
    // A state change of an integration or the enabled flag of a schedule is named by the transition, so a request that changes
    // the state and other fields records one row under the transition's action.
    public const string IntegrationCreated = "integration.created";
    public const string IntegrationUpdated = "integration.updated";
    public const string IntegrationActivated = "integration.activated";
    public const string IntegrationDeactivated = "integration.deactivated";
    public const string IntegrationRetired = "integration.retired";
    public const string BindingCreated = "binding.created";
    public const string BindingDeleted = "binding.deleted";
    public const string ScheduleCreated = "schedule.created";
    public const string ScheduleUpdated = "schedule.updated";
    public const string ScheduleEnabled = "schedule.enabled";
    public const string ScheduleDisabled = "schedule.disabled";
    public const string ScheduleAssigned = "schedule.assigned";

    // AUD-1.4 slice 5: connections. All six were declared by AUD-1.0 as legacy-bridge entries and are converted here; the secret
    // action is new and is the Secrets-category row of the unit that stores a connection's secret material. A request that rotates
    // any secret field is recorded as connection.secret_rotated, otherwise as connection.updated. A browse that ended in an
    // exception or a cancellation is a separate attempt action, since the browse itself is a required event delivered with its result.
    public const string ConnectionCreated = "connection.created";
    public const string ConnectionUpdated = "connection.updated";
    public const string ConnectionSecretRotated = "connection.secret_rotated";
    public const string ConnectionRetired = "connection.retired";
    public const string ConnectionVerified = "connection.verified";
    public const string ConnectionStructureBrowsed = "connection.structure_browsed";
    public const string ConnectionStructureBrowseFailed = "connection.structure_browse_failed";
    public const string ConnectionSecretStored = "connection.secret.stored";

    // SEC-2.2: a connection's material was refused because a secret field's reference does not name a secret that field owns.
    public const string ConnectionSecretResolutionRefused = "connection.secret.resolution_refused";

    // SEC-2.3: a setting's secret was refused because its row's reference does not name a secret that setting owns.
    public const string IntegrationSettingSecretResolutionRefused = "integration.settings.secret.resolution_refused";
    public const string PlatformSettingSecretResolutionRefused = "platform.settings.secret.resolution_refused";

    // AUD-1.4 slice 5: resource contexts. Declared by AUD-1.0 as legacy-bridge entries, converted here.
    public const string ResourceContextCreated = "resource_context.created";
    public const string ResourceContextUpdated = "resource_context.updated";
    public const string ResourceContextArchived = "resource_context.archived";
    public const string ResourceContextUnarchived = "resource_context.unarchived";

    // AUD-1.4 slice 5: ownership moves. Declared by AUD-1.0 as legacy-bridge entries, converted here. The moved_out row identifies
    // the operation (the source context is its related reference); moved_in is its secondary row (the target is its related
    // reference); a rejected move is a separate attempt row of the same principal.
    public const string IntegrationMovedOut = "integration.ownership.moved_out";
    public const string IntegrationMovedIn = "integration.ownership.moved_in";
    public const string IntegrationMoveRejected = "integration.ownership.move_rejected";
    public const string ConnectionMovedOut = "connection.ownership.moved_out";
    public const string ConnectionMovedIn = "connection.ownership.moved_in";
    public const string ConnectionMoveRejected = "connection.ownership.move_rejected";
    public const string ScheduleMovedOut = "schedule.ownership.moved_out";
    public const string ScheduleMovedIn = "schedule.ownership.moved_in";
    public const string ScheduleMoveRejected = "schedule.ownership.move_rejected";

    // AUD-1.4 slice 6: notification definitions and their subscriptions. notification.definition.deleted was declared by AUD-1.0 as a
    // legacy-bridge entry and is converted here. The definition (or subscription) is the target. A user opt-out targets the opt-out row.
    public const string NotificationDefinitionCreated = "notification.definition.created";
    public const string NotificationDefinitionUpdated = "notification.definition.updated";
    public const string NotificationDefinitionEnabled = "notification.definition.enabled";
    public const string NotificationDefinitionDisabled = "notification.definition.disabled";
    public const string NotificationDefinitionDeleted = "notification.definition.deleted";
    public const string NotificationSubscriptionAdded = "notification.subscription.added";
    public const string NotificationSubscriptionRemoved = "notification.subscription.removed";
    public const string NotificationSubscriptionUpdated = "notification.subscription.updated";
    public const string NotificationSubscriptionUserOptOutSet = "notification.subscription.user_opt_out_set";
    public const string NotificationSubscriptionUserOptOutRemoved = "notification.subscription.user_opt_out_removed";

    // AUD-1.4 slice 6: managed contacts (the notification recipient directory). Contact addresses and display names belong to a third
    // party and are never recorded; a contact is identified by its id.
    public const string ManagedContactCreated = "managed_contact.created";
    public const string ManagedContactUpdated = "managed_contact.updated";
    public const string ManagedContactUserLinked = "managed_contact.user_linked";
    public const string ManagedContactUserUnlinked = "managed_contact.user_unlinked";
    public const string ManagedContactGroupImported = "managed_contact.group_imported";

    // AUD-1.4 slice 6: notification diagnostics. A live SMTP test or pipeline test is an external attempt (W3): the row says what
    // the platform observed, never that a message was delivered. A test that was refused before any external call is a separate
    // attempt row.
    public const string NotificationSmtpTestAttempted = "notification.diagnostics.smtp_test_attempted";
    public const string NotificationPipelineTestAttempted = "notification.diagnostics.pipeline_test_attempted";
    public const string NotificationDiagnosticsTestRefused = "notification.diagnostics.test_refused";

    // AUD-1.4 slice 7: the runtime shutdown request was declared by an earlier epic as a legacy-bridge entry and is converted here.
    // Its row is the request: a duplicate request records nothing.
    public const string RuntimeShutdownRequestCreated = "governance.runtime.shutdown.request.created";

    // AUD-1.4 slice 7: attention routing. The instance default targets the instance; an override targets its integration. An
    // override upsert is recorded as created or updated depending on the row it finds under the lock.
    public const string AttentionRoutingDefaultUpdated = "attention_routing.default.updated";
    public const string AttentionRoutingOverrideCreated = "attention_routing.override.created";
    public const string AttentionRoutingOverrideUpdated = "attention_routing.override.updated";
    public const string AttentionRoutingOverrideCleared = "attention_routing.override.cleared";

    // AUD-1.4 slice 7: module lifecycle. The four changed-state actions were declared by earlier epics as legacy-bridge entries and
    // are converted here; the blocked attempts are new W3 rows naming blocker codes only. Discovery stays a legacy telemetry row.
    public const string ModuleInstalled = "module.lifecycle.install";
    public const string ModuleEnabled = "module.lifecycle.enable";
    public const string ModuleDisabled = "module.lifecycle.disable";
    public const string ModuleUninstalled = "module.lifecycle.uninstall";
    public const string ModuleInstallBlocked = "module.lifecycle.install_blocked";
    public const string ModuleEnableBlocked = "module.lifecycle.enable_blocked";
    public const string ModuleDisableBlocked = "module.lifecycle.disable_blocked";
    public const string ModuleUninstallBlocked = "module.lifecycle.uninstall_blocked";

    // SEC-2.1: an operator acknowledged an active attention incident. It targets the incident's integration (the closed target
    // vocabulary has no attention incident) and carries the incident id as a field.
    public const string AttentionIncidentAcknowledged = "attention_incident.acknowledged";

    /// <summary>The primary actions an override upsert may be recorded under, depending on whether the override already existed.</summary>
    public static IReadOnlyList<string> AttentionRoutingOverrideUpsertActions { get; } =
        [AttentionRoutingOverrideCreated, AttentionRoutingOverrideUpdated];

    /// <summary>The primary actions a definition enable or disable request may be recorded under.</summary>
    public static IReadOnlyList<string> NotificationDefinitionEnableActions { get; } =
        [NotificationDefinitionEnabled, NotificationDefinitionDisabled];

    /// <summary>The primary actions a connection update may be recorded under, depending on whether it rotates a secret field.</summary>
    public static IReadOnlyList<string> ConnectionUpdateActions { get; } = [ConnectionUpdated, ConnectionSecretRotated];

    /// <summary>The primary actions an integration update may be recorded under, depending on the state transition it makes.</summary>
    public static IReadOnlyList<string> IntegrationUpdateActions { get; } =
        [IntegrationUpdated, IntegrationActivated, IntegrationDeactivated, IntegrationRetired];

    /// <summary>The primary actions a schedule update may be recorded under, depending on whether it changes the enabled flag.</summary>
    public static IReadOnlyList<string> ScheduleUpdateActions { get; } = [ScheduleUpdated, ScheduleEnabled, ScheduleDisabled];
}
