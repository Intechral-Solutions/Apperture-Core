namespace Apperture.Core.Application.Audit;

/// <summary>
/// Detail schemas of the actions converted to the recorder (gate §12), referenced by the catalog entries. Each field is
/// bounded by the persisted limit of the value it copies, so a legitimate value never fails a write.
/// </summary>
/// <remarks>
/// A class of its own, not a part of <see cref="AuditActionCatalogEntries"/>: static initializers of a partial class run in
/// an unspecified order across files, so the catalog list could otherwise capture a schema before it is built. This class is
/// initialized on first access, which the catalog list's initializer performs.
/// </remarks>
internal static class AuditDetailSchemas
{
    /// <summary>Stored names (display, role and user names) are at most 256 characters in the database.</summary>
    private const int StoredNameLength = 256;

    /// <summary>A permission identity (<c>action@resourceType:scopeKey</c>) can exceed an ordinary id when a module scope key is long.</summary>
    internal const int PermissionIdentityLength = 512;

    /// <summary>More than any role can hold: roles can only contain permissions of the registered catalog.</summary>
    internal const int MaxPermissionsPerDiff = 2048;

    private static readonly string[] IdentitySources = ["entra", "local"];

    private static AuditFieldDefinition IdField(string name, bool required = true) =>
        new(name, AuditFieldKind.Id, required);

    private static AuditFieldDefinition UsernameField(string name, bool required = true) =>
        new(name, AuditFieldKind.Username, required);

    private static AuditFieldDefinition NameField(string name, bool required = true) =>
        new(name, AuditFieldKind.NameSnapshot, required, MaxLength: StoredNameLength, TruncatedLength: 64);

    private static AuditFieldDefinition FlagField(string name, bool required = true) =>
        new(name, AuditFieldKind.Flag, required);

    private static AuditFieldDefinition CountField(string name, bool required = true) =>
        new(name, AuditFieldKind.Count, required);

    private static AuditFieldDefinition EnumField(string name, bool required, params string[] values) =>
        new(name, AuditFieldKind.Enum, required, EnumValues: new HashSet<string>(values, StringComparer.Ordinal));

    private static AuditFieldDefinition PermissionListField(string name) =>
        new(name, AuditFieldKind.IdList, Required: false, MaxLength: PermissionIdentityLength, MaxItems: MaxPermissionsPerDiff);

    // ---- Roles and permissions ----

    internal static readonly AuditDetailSchema RoleCreated = new(
    [
        IdField("roleId"),
        NameField("name"),
        CountField("permissionCount"),
        PermissionListField("permissionsAdded"),
    ]);

    internal static readonly AuditDetailSchema RoleMetadataUpdated = new(
    [
        IdField("roleId"),
        NameField("previousName"),
        NameField("newName"),
        FlagField("descriptionChanged"),
    ]);

    internal static readonly AuditDetailSchema RoleDeleted = new(
    [
        IdField("roleId"),
        NameField("name"),
    ]);

    internal static readonly AuditDetailSchema PermissionAssignmentUpdated = new(
    [
        IdField("roleId"),
        CountField("permissionCount"),
        PermissionListField("permissionsAdded"),
        PermissionListField("permissionsRemoved"),
    ]);

    internal static readonly AuditDetailSchema RoleMutationRejected = new(
    [
        IdField("roleId"),
        EnumField("reason", true, "system_role", "assignments_remain"),
        CountField("assignmentCount", required: false),
    ]);

    // ---- Users, assignments, identity links and credentials ----

    internal static readonly AuditDetailSchema UserCreated = new(
    [
        IdField("userId"),
        UsernameField("username"),
        NameField("displayName"),
        FlagField("localSignInReady"),
        FlagField("setupInvitationRequested"),
    ]);

    internal static readonly AuditDetailSchema RoleAssignment = new(
    [
        IdField("userId"),
        IdField("roleId"),
    ]);

    internal static readonly AuditDetailSchema EntraLinkPending = new(
    [
        IdField("userId"),
        IdField("linkId"),
        IdField("tenantId"),
        IdField("objectId"),
    ]);

    internal static readonly AuditDetailSchema IdentityLinkChange = new(
    [
        IdField("userId"),
        IdField("linkId"),
        EnumField("source", true, IdentitySources),
        UsernameField("username", required: false),
        IdField("objectId", required: false),
        IdField("tenantId", required: false),
    ]);

    internal static readonly AuditDetailSchema UserOnly = new(
    [
        IdField("userId"),
    ]);

    internal static readonly AuditDetailSchema BlockedLastAdminLinkRemoved = new(
    [
        IdField("userId"),
        IdField("linkId"),
        EnumField("source", true, IdentitySources),
    ]);

    internal static readonly AuditDetailSchema StrongAuthenticationInsufficient = new(
    [
        IdField("userId"),
        IdField("code"),
    ]);

    internal static readonly AuditDetailSchema UnsafeOperationBlocked = new(
    [
        IdField("userId"),
        IdField("operation"),
    ]);

    // ---- Recovery Access administration ----

    internal static readonly AuditDetailSchema RecoveryAccessDesignated = new(
    [
        IdField("userId"),
        IdField("previousUserId", required: false),
    ]);

    internal static readonly AuditDetailSchema RecoveryAccessProvisioned = new(
    [
        IdField("userId"),
        UsernameField("username"),
    ]);

    // ---- Native MFA ----

    internal static readonly AuditDetailSchema RecoveryCodesGenerated = new(
    [
        IdField("userId"),
        CountField("count"),
        FlagField("regenerated"),
    ]);

    internal static readonly AuditDetailSchema RecoveryCodeConsumed = new(
    [
        IdField("userId"),
        CountField("remaining"),
    ]);

    // ---- Account services ----
    // requestId is the id of the one-time request row (AccountActionTokens.Id). The link's plaintext, its hash and the
    // contact address are never recorded.

    /// <summary>The purposes of a one-time account request, as recorded.</summary>
    internal static readonly string[] AccountRequestPurposes = ["setup_invitation", "contact_verification", "password_reset"];

    internal static readonly AuditDetailSchema AccountRequestIssued = new(
    [
        IdField("userId"),
        IdField("requestId"),
    ]);

    internal static readonly AuditDetailSchema ContactVerificationRequested = new(
    [
        IdField("userId"),
        IdField("requestId"),
        FlagField("resent"),
    ]);

    internal static readonly AuditDetailSchema ContactEmailVerified = new(
    [
        IdField("userId"),
        EnumField("via", true, "verification_link", "setup_invitation"),
    ]);

    internal static readonly AuditDetailSchema SecurityMailDeliveryAttempted = new(
    [
        IdField("userId"),
        IdField("requestId"),
        EnumField("purpose", true, AccountRequestPurposes),
        EnumField("failure", false, "not_configured", "send_failed", "error"),
    ]);

    internal static readonly AuditDetailSchema Empty = new([]);

    internal static readonly AuditDetailSchema DisplayNameUpdated = new(
    [
        IdField("userId"),
        NameField("previousDisplayName"),
        NameField("newDisplayName"),
    ]);

    internal static readonly AuditDetailSchema AvatarReset = new(
    [
        IdField("userId"),
        EnumField("avatar", true, "default", "built_in"),
        CountField("imagesDeleted"),
    ]);

    internal static readonly AuditDetailSchema AvatarUpdated = new(
    [
        IdField("userId"),
        IdField("imageKey"),
        CountField("imagesDeleted"),
    ]);

    internal static readonly AuditDetailSchema AvatarUploadRejected = new(
    [
        IdField("userId"),
        EnumField(
            "reason",
            true,
            "empty", "too_large", "unsupported_declared_type", "undecodable_image", "unsupported_image_format",
            "dimensions_too_large", "dimensions_too_small", "encode_failed"),
    ]);

    // ---- Provisioning and access requests ----
    // A Microsoft identity is its tenant id and object id (gate §10.4); email, UPN and display name are never keys.

    internal static readonly AuditDetailSchema ProvisionedFromDirectory = new(
    [
        IdField("userId"),
        UsernameField("username"),
        IdField("tenantId"),
        IdField("objectId"),
        IdField("registrationId"),
    ]);

    internal static readonly AuditDetailSchema ProvisionedFromAccessRequest = new(
    [
        IdField("userId"),
        UsernameField("username"),
        IdField("tenantId"),
        IdField("objectId"),
        IdField("requestId"),
    ]);

    internal static readonly AuditDetailSchema AccessRequestCreated = new(
    [
        IdField("requestId"),
        IdField("registrationId"),
        IdField("tenantId"),
        IdField("objectId"),
    ]);

    internal static readonly AuditDetailSchema AccessRequestDecision = new(
    [
        IdField("requestId"),
        IdField("tenantId"),
        IdField("objectId"),
        IdField("userId", required: false),
    ]);

    internal static readonly AuditDetailSchema Welcome = new(
    [
        IdField("userId"),
        EnumField("failure", false, "not_configured", "send_failed", "error"),
    ]);

    // ---- Entra association ----

    internal static readonly AuditDetailSchema EntraLinkChange = new(
    [
        IdField("userId"),
        IdField("linkId"),
        IdField("tenantId"),
        IdField("objectId"),
    ]);

    internal static readonly AuditDetailSchema EntraLinkRefused = new(
    [
        IdField("userId"),
        IdField("tenantId"),
        IdField("objectId"),
        EnumField("reason", true, "emergency_account", "user_already_has_entra_link", "owned_by_other_user", "concurrent_insert"),
    ]);

    internal static readonly AuditDetailSchema EntraTenantMismatch = new(
    [
        IdField("tenantId"),
        IdField("objectId"),
        EnumField("flow", true, "sign_in", "link"),
        IdField("userId", required: false),
    ]);

    internal static readonly AuditDetailSchema ExternalIdentityOnly = new(
    [
        IdField("tenantId"),
        IdField("objectId"),
    ]);

    internal static readonly AuditDetailSchema LocalDisabledEntraPrimary = new(
    [
        IdField("userId"),
        FlagField("recoveryAccessHealthy"),
    ]);

    // ---- Authentication ----

    internal static readonly AuditDetailSchema LocalSignIn = new(
    [
        IdField("userId"),
        EnumField("mechanism", true, "password", "native_totp", "native_recovery_code"),
    ]);

    internal static readonly AuditDetailSchema EntraSignIn = new(
    [
        IdField("userId"),
        IdField("tenantId"),
        IdField("objectId"),
    ]);

    internal static readonly AuditDetailSchema EntraSignInFailed = new(
    [
        EnumField("flow", true, "sign_in", "link", "step_up", "unknown"),
        EnumField("reason", true, "cancelled", "failed"),
    ]);

    internal static readonly AuditDetailSchema SignedOut = new(
    [
        IdField("userId"),
        EnumField("session", true, "local", "entra"),
    ]);

    internal static readonly AuditDetailSchema LocalPasswordChangeRefused = new(
    [
        IdField("userId"),
        EnumField(
            "reason",
            true,
            "not_linked", "local_auth_disabled", "missing_new_password", "user_not_found", "system_managed", "no_local_identity",
            "no_credential", "credential_disabled", "verification_required", "wrong_current_password", "policy_rejected"),
    ]);

    internal static readonly AuditDetailSchema StepUpInitiated = new(
    [
        IdField("userId"),
        IdField("requestedContext"),
        EnumField("session", true, "local", "entra"),
    ]);

    internal static readonly AuditDetailSchema StepUpFailed = new(
    [
        IdField("userId"),
        EnumField(
            "reason",
            true,
            "invalid_identity", "not_configured", "unproven", "continuation_invalid", "continuation_stale", "user_not_found",
            "no_local_identity", "local_session_unavailable"),
        IdField("tenantId", required: false),
        IdField("objectId", required: false),
    ]);

    internal static readonly AuditDetailSchema StepUpIdentity = new(
    [
        IdField("userId"),
        IdField("tenantId"),
        IdField("objectId"),
        EnumField("reason", false, "no_link", "different_user"),
        IdField("otherUserId", required: false),
    ]);

    internal static readonly AuditDetailSchema StepUpContextNotSatisfied = new(
    [
        IdField("userId"),
        IdField("requestedContext"),
    ]);

    internal static readonly AuditDetailSchema StepUpSatisfied = new(
    [
        IdField("userId"),
        IdField("requestedContext"),
        IdField("tenantId"),
        IdField("objectId"),
        FlagField("developmentSimulated"),
    ]);

    // ---- Entra tenant registrations and the sign-in policy ----
    // Configuration values are recorded as identifiers and flags. The client secret is never recorded, only that it was
    // replaced; the authority URL and callback path are recorded only as having changed.

    /// <summary>The settings of a tenant registration a change can name.</summary>
    internal static readonly string[] TenantRegistrationSettings =
    [
        "displayName", "authority", "clientId", "clientAuthentication", "usesDeploymentSecret", "callbackPath", "azureTenantId",
        "authenticationContextId", "enabled", "allowMemberAccessRequests", "allowGuestAccessRequests",
    ];

    internal static readonly AuditDetailSchema TenantRegistrationCreated = new(
    [
        IdField("registrationId"),
        NameField("displayName"),
        IdField("clientId"),
        IdField("azureTenantId", required: false),
        FlagField("enabled"),
        FlagField("clientAuthenticationReplaced"),
        FlagField("allowMemberAccessRequests"),
        FlagField("allowGuestAccessRequests"),
    ]);

    internal static readonly AuditDetailSchema TenantRegistrationUpdated = new(
    [
        IdField("registrationId"),
        NameField("displayName"),
        new AuditFieldDefinition("changed", AuditFieldKind.IdList, Required: true, MaxItems: 16),
        FlagField("enabled"),
        FlagField("active"),
    ]);

    internal static readonly AuditDetailSchema TenantRegistrationDeleted = new(
    [
        IdField("registrationId"),
        NameField("displayName"),
        FlagField("wasActive"),
    ]);

    internal static readonly AuditDetailSchema TenantRegistrationActivated = new(
    [
        IdField("registrationId"),
        IdField("previousRegistrationId", required: false),
    ]);

    internal static readonly AuditDetailSchema TenantRegistrationChangeRefused = new(
    [
        IdField("registrationId"),
        EnumField("change", true, "update", "activate", "delete"),
        EnumField("reason", true, "sign_in_lockout", "registration_disabled"),
    ]);

    internal static readonly AuditDetailSchema AccessRequestPolicyChanged = new(
    [
        IdField("registrationId"),
        FlagField("allowMemberAccessRequests"),
        FlagField("allowGuestAccessRequests"),
    ]);

    internal static readonly AuditDetailSchema SignInPolicyUpdated = new(
    [
        new AuditFieldDefinition("changed", AuditFieldKind.IdList, Required: true, MaxItems: 8),
        FlagField("showLocalSignInInPrimaryChooser"),
        FlagField("preferEntraInChooser"),
        FlagField("autoRedirectToEntra"),
    ]);

    // ---- First-administrator bootstrap and bootstrap recovery ----

    internal static readonly AuditDetailSchema FirstAdminCreated = new(
    [
        IdField("userId"),
        UsernameField("username"),
        FlagField("localSignInReady"),
    ]);

    internal static readonly AuditDetailSchema ConfiguredAdminCreated = new(
    [
        IdField("userId"),
        UsernameField("username"),
        EnumField("identitySource", true, "local", "entra"),
        IdField("roleId"),
    ]);

    internal static readonly AuditDetailSchema FirstAdminRejectedInvalidRequest = new(
    [
        EnumField("reason", true, "missing_fields", "invalid_identity_source", "password_policy", "username_policy"),
    ]);

    internal static readonly AuditDetailSchema RestorePlatformAccessExecuted = new(
    [
        IdField("userId"),
        IdField("roleId"),
    ]);

    internal static readonly AuditDetailSchema RestorePlatformAccessRefused = new(
    [
        EnumField("reason", true, "not_authorized", "target_not_found"),
    ]);

    // ---- Runtime enrollment ----
    // invitationId is the invitation row's id; the enrollment token itself is never recorded.

    internal static readonly AuditDetailSchema RuntimeEnrollmentInvitationCreated = new(
    [
        IdField("runtimeId"),
        IdField("invitationId"),
        CountField("capabilityCount"),
    ]);

    internal static readonly AuditDetailSchema RuntimeEnrollmentCompleted = new(
    [
        IdField("runtimeId"),
        IdField("invitationId"),
    ]);

    internal static readonly AuditDetailSchema NotificationPreferenceChanged = new(
    [
        IdField("userId"),
        IdField("definitionId"),
        EnumField("channel", true, "email", "toast"),
    ]);

    // ---- AUD-1.3: operator actions on runs ----
    // A start records the names of its parameters and never their values (gate §12.3). Ids copied from a request are bounded
    // by the persisted column they were stored in, and an id the schema cannot hold is left out by the producer rather than
    // failing a refusal row.

    /// <summary>Most parameter names one start row lists. The count of every parameter is recorded as well.</summary>
    internal const int MaxParameterNames = 64;

    /// <summary>Longest parameter name listed; a longer name is counted but not listed.</summary>
    internal const int MaxParameterNameLength = 64;

    private static readonly string[] LaunchChannels = ["direct", "procedure"];

    internal static readonly AuditDetailSchema StartRequested = new(
    [
        IdField("runId"),
        IdField("instanceId"),
        IdField("definitionId"),
        IdField("integrationId", required: false),
        IdField("procedureId", required: false),
        EnumField("launch", true, LaunchChannels),
        CountField("parameterCount"),
        new AuditFieldDefinition("parameterNames", AuditFieldKind.IdList, Required: false, MaxLength: MaxParameterNameLength, MaxItems: MaxParameterNames),
    ]);

    internal static readonly AuditDetailSchema StartBlocked = new(
    [
        IdField("definitionId", required: false),
        IdField("integrationId", required: false),
        IdField("procedureId", required: false),
        EnumField("launch", true, LaunchChannels),
        EnumField("reason", true, "definition_not_executable"),
    ]);

    /// <summary>Cancel, park and resume: a request on an existing run, with the run's status when it was made.</summary>
    internal static readonly AuditDetailSchema RunControlRequested = new(
    [
        IdField("runId"),
        IdField("instanceId"),
        IdField("definitionId"),
        IdField("runStatus"),
    ]);

    internal static readonly AuditDetailSchema WorkflowRetryRequested = new(
    [
        IdField("runId"),
        IdField("retryRunId"),
        IdField("definitionId"),
    ]);

    internal static readonly AuditDetailSchema WorkflowRetryBlocked = new(
    [
        IdField("runId"),
        IdField("definitionId"),
        EnumField("reason", true, "definition_not_executable"),
    ]);

    internal static readonly AuditDetailSchema OperationItemRetry = new(
    [
        IdField("operationItemId"),
        IdField("runId"),
        IdField("operationGroupId"),
        IdField("attemptId"),
        CountField("attemptIndex"),
        IdField("childRunId", required: false),
        IdField("errorCode", required: false),
        new AuditFieldDefinition("reason", AuditFieldKind.OperatorText, Required: false, MaxLength: OperationItems.OperationItemRetryBounds.ReasonMaxLength),
    ]);

    internal static readonly AuditDetailSchema RecoveryRetryRequested = new(
    [
        IdField("runId"),
        IdField("retryRunId"),
        IdField("definitionId"),
    ]);

    internal static readonly AuditDetailSchema RecoveryRetryRefused = new(
    [
        IdField("runId"),
        IdField("definitionId", required: false),
        EnumField("reason", true, "module_not_executable", "definition_not_mapped", "definition_not_executable"),
    ]);

    /// <summary>A recovery retry the caller was not permitted to make. The run id is the caller's own text, so it is optional.</summary>
    internal static readonly AuditDetailSchema RecoveryRetryDenied = new(
    [
        IdField("runId", required: false),
        EnumField("reason", true, "permission_missing"),
    ]);

    internal static readonly AuditDetailSchema SchedulerOperationDenied = new(
    [
        EnumField("operation", true, "run_cycle", "recover_stale"),
        EnumField("reason", true, "permission_missing"),
    ]);

    /// <summary>A manual scheduler operation. The plane id is the caller's own text, so it is optional.</summary>
    internal static readonly AuditDetailSchema SchedulerOperationRequested = new(
    [
        EnumField("operation", true, "run_cycle", "recover_stale"),
        IdField("runtimePlaneId", required: false),
        CountField("recoveredCount", required: false),
    ]);
}
