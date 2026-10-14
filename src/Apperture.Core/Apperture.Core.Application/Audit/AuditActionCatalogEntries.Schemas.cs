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
        IdField("bulkRequestId", required: false),
    ]);

    /// <summary>The items one acknowledge or reopen request changed: a bulk request is bounded, so the list is too.</summary>
    private static AuditFieldDefinition OperationItemIdsField() =>
        new("operationItemIds", AuditFieldKind.IdList, Required: true, MaxLength: OperationItems.OperationItemAttentionBounds.IdMaxLength, MaxItems: OperationItems.OperationItemAttentionBounds.MaxBulkItemCount);

    internal static readonly AuditDetailSchema OperationItemAttentionAcknowledged = new(
    [
        IdField("runId"),
        IdField("operationGroupId"),
        EnumField("disposition", true, [.. OperationItems.OperationItemAttentionDispositions.All]),
        new AuditFieldDefinition("reason", AuditFieldKind.OperatorText, Required: false, MaxLength: OperationItems.OperationItemAttentionBounds.ReasonMaxLength),
        CountField("requestedCount"),
        CountField("acknowledgedCount"),
        CountField("skippedCount"),
        OperationItemIdsField(),
    ]);

    internal static readonly AuditDetailSchema OperationItemAttentionReopened = new(
    [
        IdField("runId"),
        IdField("operationGroupId"),
        CountField("requestedCount"),
        CountField("reopenedCount"),
        CountField("skippedCount"),
        OperationItemIdsField(),
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

    // ---- AUD-1.4: secret-store events (Secrets category) ----
    // The target and every id are the configuration entity that owns the secret (gate §22 ruling on secret audit
    // identifiers). A secret's store id is the reference its owner holds, so no field carries it, and no field name can
    // describe secret content (AuditDetailSchema.IsSensitiveFieldName).

    internal static readonly AuditDetailSchema TenantRegistrationClientSecretStored = new(
    [
        IdField("registrationId"),
        EnumField("change", true, "registration_created", "registration_updated"),
    ]);

    internal static readonly AuditDetailSchema TenantRegistrationClientSecretRetired = new(
    [
        IdField("registrationId"),
        EnumField("reason", true, "replaced", "registration_deleted"),
    ]);

    // ---- AUD-1.4: platform and integration settings ----
    // A setting's value is recorded only for an ordinary setting, as its display text, bounded and truncatable. The value of a
    // sensitive setting, the secret and the secret's store id (the reference the setting row holds) are never fields, and no
    // field name can describe secret content (AuditDetailSchema.IsSensitiveFieldName). A secret event names the setting that
    // owns the secret and carries closed values only.

    /// <summary>An ordinary setting's value as text. A value is at most 512 characters stored; its display text adds list separators.</summary>
    private static AuditFieldDefinition SettingValueField(string name) =>
        new(name, AuditFieldKind.NameSnapshot, Required: false, MaxLength: 1024, TruncatedLength: 256);

    private static readonly string[] SettingSetOperations = ["set", "replace", "clear"];

    internal static readonly AuditDetailSchema PlatformSettingSet = new(
    [
        IdField("key"),
        EnumField("operation", true, SettingSetOperations),
        FlagField("sensitive"),
        SettingValueField("previousValue"),
        SettingValueField("newValue"),
        FlagField("previousConfigured", required: false),
    ]);

    internal static readonly AuditDetailSchema PlatformSettingReset = new(
    [
        IdField("key"),
        FlagField("sensitive"),
        SettingValueField("previousValue"),
        FlagField("previousConfigured", required: false),
    ]);

    internal static readonly AuditDetailSchema IntegrationSettingSet = new(
    [
        IdField("integrationId"),
        IdField("key"),
        EnumField("operation", true, SettingSetOperations),
        FlagField("sensitive"),
        SettingValueField("previousValue"),
        SettingValueField("newValue"),
        FlagField("previousConfigured", required: false),
        IdField("optionProviderKey", required: false),
    ]);

    internal static readonly AuditDetailSchema IntegrationSettingReset = new(
    [
        IdField("integrationId"),
        IdField("key"),
        FlagField("sensitive"),
        SettingValueField("previousValue"),
        FlagField("previousConfigured", required: false),
    ]);

    internal static readonly AuditDetailSchema PlatformSettingSecretStored = new(
    [
        IdField("key"),
        EnumField("change", true, "created", "replaced"),
    ]);

    internal static readonly AuditDetailSchema PlatformSettingSecretRetired = new(
    [
        IdField("key"),
        EnumField("reason", true, "cleared", "reset"),
    ]);

    internal static readonly AuditDetailSchema IntegrationSettingSecretStored = new(
    [
        IdField("integrationId"),
        IdField("key"),
        EnumField("change", true, "created", "replaced"),
    ]);

    internal static readonly AuditDetailSchema IntegrationSettingSecretRetired = new(
    [
        IdField("integrationId"),
        IdField("key"),
        EnumField("reason", true, "cleared", "reset", "integration_deleted"),
    ]);

    // ---- Integration hard-delete (AUD-1.4 slice 3, work package 4a) ----
    // The integration is the event's target, so its id is not repeated; its owning resource context and its module are the
    // related references. The name is a snapshot, since the integration no longer exists once the row is written. The count is
    // the number of persisted integration-scoped setting overrides the delete removed, whatever their definition now says. Which
    // overrides, their values, and the secrets they referenced are never recorded here: a secret the delete retired has its own
    // Secrets-category row under the integration setting it belonged to.

    internal static readonly AuditDetailSchema IntegrationDeleted = new(
    [
        NameField("name"),
        IdField("moduleId", required: false),
        CountField("settingsRemoved"),
    ]);

    // ---- Schedule hard-delete (AUD-1.4 slice 3) ----
    // The schedule is the event's target and its related schedule reference, so the id is not repeated here. The name is a
    // snapshot: the schedule no longer exists once the row is written. Neither the recurrence nor the target configuration is
    // recorded.

    internal static readonly AuditDetailSchema ScheduleDeleted = new(
    [
        NameField("name"),
        IdField("integrationId", required: false),
        CountField("assignmentsRemoved"),
    ]);

    internal static readonly AuditDetailSchema ScheduleDeleteRejected = new(
    [
        NameField("name"),
        IdField("integrationId", required: false),
        EnumField("reason", true, "enabled"),
    ]);

    // ---- Integration, binding and schedule lifecycle (AUD-1.4 slice 4) ----
    // The entity is the event's target, so its id is not repeated. Names are snapshots. A change is described by the names of the
    // fields that differ, never by their values: an integration's settings dictionary and a schedule's recurrence, time zone,
    // target configuration and execution policy reference have no field that could carry a value.

    /// <summary>The integration fields an update can change, other than its state (named, never valued).</summary>
    internal static readonly string[] IntegrationChangeFields = ["displayName", "settings"];

    /// <summary>The schedule fields an update can change, other than the enabled flag (named, never valued).</summary>
    internal static readonly string[] ScheduleChangeFields = ["name", "recurrence", "timeZone"];

    private static readonly string[] IntegrationStateValues = ["active", "inactive", "retired"];

    internal static readonly AuditDetailSchema IntegrationCreated = new(
    [
        NameField("name"),
        IdField("moduleId", required: false),
        FlagField("settingsProvided"),
    ]);

    internal static readonly AuditDetailSchema IntegrationUpdated = new(
    [
        NameField("name"),
        IdField("moduleId", required: false),
        new AuditFieldDefinition("changed", AuditFieldKind.IdList, Required: true, MaxItems: 4),
    ]);

    internal static readonly AuditDetailSchema IntegrationStateChanged = new(
    [
        NameField("name"),
        IdField("moduleId", required: false),
        EnumField("fromState", true, IntegrationStateValues),
        EnumField("toState", true, IntegrationStateValues),
        new AuditFieldDefinition("changed", AuditFieldKind.IdList, Required: false, MaxItems: 4),
    ]);

    internal static readonly AuditDetailSchema BindingChanged = new(
    [
        IdField("moduleId"),
        IdField("slotName"),
        IdField("integrationId", required: false),
        IdField("connectionId", required: false),
        IdField("capabilityId", required: false),
    ]);

    internal static readonly AuditDetailSchema ScheduleCreated = new(
    [
        NameField("name"),
        IdField("integrationId", required: false),
        FlagField("enabled"),
        IdField("runtimePlaneId", required: false),
        CountField("assignmentsCreated"),
    ]);

    internal static readonly AuditDetailSchema ScheduleUpdated = new(
    [
        NameField("name"),
        IdField("integrationId", required: false),
        new AuditFieldDefinition("changed", AuditFieldKind.IdList, Required: true, MaxItems: 4),
    ]);

    internal static readonly AuditDetailSchema ScheduleEnabledChanged = new(
    [
        NameField("name"),
        IdField("integrationId", required: false),
        new AuditFieldDefinition("changed", AuditFieldKind.IdList, Required: false, MaxItems: 4),
    ]);

    internal static readonly AuditDetailSchema ScheduleAssigned = new(
    [
        IdField("runtimePlaneId"),
        FlagField("policyOverridesSet"),
    ]);

    // ---- Notification definitions, subscriptions, managed contacts and diagnostics (AUD-1.4 slice 6) ----
    // A definition's name is a snapshot; its rule and template (free text with subjects, bodies and links) are never recorded, only
    // the names of the parts that changed. A subscription is identified by its definition and its recipient's id (a user, team or
    // contact id, never an address). A managed contact's address, display name and directory identity are a third party's and have
    // no field at all.

    private static readonly string[] RecipientKinds = ["principal", "operationalTeam", "managedContact"];

    private static readonly string[] DeliveryModes = ["immediate", "muted", "digestOnly", "immediateAndDigest"];

    internal static readonly AuditDetailSchema NotificationDefinitionCreated = new(
    [
        NameField("name"),
        IdField("integrationId", required: false),
        FlagField("enabled"),
        FlagField("toastEnabledByDefault"),
        IdField("defaultSeverity", required: false),
        IdField("primaryEventType", required: false),
    ]);

    internal static readonly AuditDetailSchema NotificationDefinitionUpdated = new(
    [
        NameField("name"),
        IdField("integrationId", required: false),
        FieldKeysField("changedFields", required: true),
    ]);

    internal static readonly AuditDetailSchema NotificationDefinitionEnableChanged = new(
    [
        NameField("name"),
        IdField("integrationId", required: false),
    ]);

    internal static readonly AuditDetailSchema NotificationDefinitionDeleted = new(
    [
        NameField("name"),
        IdField("integrationId", required: false),
        CountField("subscriptionsRemoved"),
        CountField("userOptOutsRemoved"),
        CountField("userPreferencesRemoved"),
    ]);

    internal static readonly AuditDetailSchema NotificationSubscriptionAdded = new(
    [
        IdField("definitionId"),
        IdField("integrationId", required: false),
        EnumField("recipientKind", true, RecipientKinds),
        IdField("recipientRef"),
        EnumField("deliveryMode", true, DeliveryModes),
        FlagField("managedContactCreated", required: false),
    ]);

    internal static readonly AuditDetailSchema NotificationSubscriptionRemoved = new(
    [
        IdField("definitionId"),
        IdField("integrationId", required: false),
        EnumField("recipientKind", true, RecipientKinds),
        IdField("recipientRef"),
    ]);

    internal static readonly AuditDetailSchema NotificationSubscriptionUpdated = new(
    [
        IdField("definitionId"),
        IdField("integrationId", required: false),
        EnumField("change", true, "enabled", "disabled", "delivery_mode"),
        EnumField("deliveryMode", false, DeliveryModes),
    ]);

    internal static readonly AuditDetailSchema NotificationSubscriptionUserOptOut = new(
    [
        IdField("definitionId"),
        IdField("userId"),
        IdField("operationalTeamId", required: false),
    ]);

    internal static readonly AuditDetailSchema ManagedContactCreated = new(
    [
        EnumField("source", true, "custom", "entra"),
        EnumField("via", true, "admin", "subscription", "entra_import"),
        FlagField("enabled"),
        FlagField("hasEmail"),
    ]);

    internal static readonly AuditDetailSchema ManagedContactUpdated = new(
    [
        EnumField("source", true, "custom", "entra"),
        FieldKeysField("changedFields", required: true),
        FlagField("enabled"),
    ]);

    internal static readonly AuditDetailSchema ManagedContactUserLinked = new(
    [
        IdField("userId"),
        IdField("previousUserId", required: false),
    ]);

    internal static readonly AuditDetailSchema ManagedContactUserUnlinked = new(
    [
        IdField("previousUserId"),
    ]);

    internal static readonly AuditDetailSchema ManagedContactGroupImported = new(
    [
        FlagField("created"),
    ]);

    /// <summary>What the transport reported about one send: acceptance by the server is not delivery to a mailbox.</summary>
    internal static readonly string[] SendResults = ["accepted", "rejected", "failed", "no_answer"];

    internal static readonly AuditDetailSchema NotificationSmtpTestAttempted = new(
    [
        EnumField("result", true, SendResults),
    ]);

    internal static readonly AuditDetailSchema NotificationPipelineTestAttempted = new(
    [
        IdField("integrationId", required: false),
        FlagField("dispatchRequested"),
        CountField("instancesCreated"),
        CountField("outboxRowsCreated"),
        CountField("sendsAttempted"),
        CountField("sendsAccepted"),
        CountField("sendsRejected"),
        CountField("sendsFailed"),
        CountField("sendsUnknown"),
    ]);

    internal static readonly AuditDetailSchema NotificationDiagnosticsTestRefused = new(
    [
        EnumField("test", true, "smtp", "pipeline"),
        EnumField("reason", true, "invalid_input", "not_configured", "delivery_disabled", "no_definition"),
    ]);

    // ---- Connections (AUD-1.4 slice 5) ----
    // The connection is the event's target and its owning resource context the related reference, so neither is repeated. Names are
    // snapshots; the connector is an id. A change is described by the names of the fields that differ, never their values: config
    // values, secret values and secret references have no field. Field names of a connector are declared by the connector (for
    // example "BaseUrl" or "Password"), are bounded, and are never a value.

    private static AuditFieldDefinition FieldKeysField(string name, bool required) =>
        new(name, AuditFieldKind.IdList, required, MaxItems: 64);

    internal static readonly AuditDetailSchema ConnectionCreated = new(
    [
        NameField("name"),
        IdField("connectorId"),
        CountField("fieldsStored"),
    ]);

    internal static readonly AuditDetailSchema ConnectionUpdated = new(
    [
        NameField("name"),
        IdField("connectorId"),
        FlagField("nameChanged", required: false),
        FieldKeysField("configChanged", required: false),
        FieldKeysField("rotated", required: false),
    ]);

    /// <summary>
    /// A Secrets-category event, so ids, closed values, flags and counts only (see the catalog rule for recorder secret events): the
    /// names of the fields stay on the connection's <c>connection.secret.stored</c> rows and on <c>connection.updated</c>, and this
    /// row counts them.
    /// </summary>
    internal static readonly AuditDetailSchema ConnectionSecretRotated = new(
    [
        IdField("connectorId"),
        FlagField("nameChanged", required: false),
        CountField("configFieldsChanged", required: false),
        CountField("fieldsRotated"),
    ]);

    internal static readonly AuditDetailSchema ConnectionRetired = new(
    [
        NameField("name"),
        IdField("connectorId"),
    ]);

    /// <summary>
    /// What the verifier answered, and nothing the platform did not observe. The status is absent when the verifier produced no
    /// answer (it threw). The verifier's own summary is remote-derived text and is never recorded.
    /// </summary>
    internal static readonly AuditDetailSchema ConnectionVerified = new(
    [
        NameField("name"),
        IdField("connectorId"),
        EnumField("verificationStatus", false, "success", "failed", "unavailable"),
    ]);

    internal static readonly string[] BrowseOperations = ["schemas", "tables", "columns"];

    internal static readonly AuditDetailSchema ConnectionStructureBrowsed = new(
    [
        NameField("name"),
        IdField("connectorId"),
        EnumField("tool", true, "sql-structure"),
        EnumField("operation", true, BrowseOperations),
        EnumField("result", true, "succeeded", "remote_denied", "remote_unavailable", "secrets_unavailable", "failed"),
        CountField("resultCount", required: false),
        FlagField("truncated", required: false),
    ]);

    internal static readonly AuditDetailSchema ConnectionStructureBrowseFailed = new(
    [
        NameField("name"),
        IdField("connectorId"),
        EnumField("tool", true, "sql-structure"),
        EnumField("operation", true, BrowseOperations),
        EnumField("reason", true, "failed", "cancelled"),
    ]);

    internal static readonly AuditDetailSchema ConnectionSecretStored = new(
    [
        IdField("fieldKey"),
        EnumField("change", true, "created", "replaced"),
    ]);

    // ---- Resource contexts (AUD-1.4 slice 5) ----
    // The context is the event's target and its own related reference. The name is a snapshot; the slug is an id.

    internal static readonly AuditDetailSchema ResourceContextCreated = new(
    [
        NameField("name"),
        IdField("slug"),
    ]);

    /// <summary>The context fields an update can change (named, never valued).</summary>
    internal static readonly string[] ResourceContextChangeFields = ["name", "slug", "description"];

    internal static readonly AuditDetailSchema ResourceContextUpdated = new(
    [
        NameField("name"),
        new AuditFieldDefinition("changed", AuditFieldKind.IdList, Required: true, MaxItems: 3),
    ]);

    internal static readonly AuditDetailSchema ResourceContextStateChanged = new(
    [
        NameField("name"),
    ]);

    // ---- Ownership moves (AUD-1.4 slice 5) ----
    // The moved resource is the target. The source context is the related reference of moved_out and of a rejection, the target
    // context that of moved_in; both ids are also fields so either row is complete on its own. An integration move also moves its
    // attached schedules: they are counted exactly and listed up to a bound, so the row can never outgrow the details limit.

    /// <summary>The most attached schedule ids one move row lists; the count is always exact.</summary>
    internal const int MaxMovedScheduleIds = 500;

    internal static readonly AuditDetailSchema OwnershipMoved = new(
    [
        NameField("name"),
        IdField("sourceContextId"),
        IdField("targetContextId"),
        CountField("schedulesMoved", required: false),
        new AuditFieldDefinition("scheduleIds", AuditFieldKind.IdList, Required: false, MaxItems: MaxMovedScheduleIds),
    ]);

    internal static readonly AuditDetailSchema OwnershipMoveRejected = new(
    [
        NameField("name"),
        IdField("sourceContextId"),
        IdField("targetContextId"),
        new AuditFieldDefinition("blockers", AuditFieldKind.IdList, Required: true, MaxItems: 16),
    ]);

    // ---- Runtime shutdown request (AUD-1.4 slice 7) ----
    // The runtime instance is the target (by scope key). The request is identified by its persisted id; the requester is the
    // row's actor and has no field.

    internal static readonly AuditDetailSchema RuntimeShutdownRequestCreated = new(
    [
        IdField("runtimeId"),
        IdField("requestId"),
        EnumField("controlKind", true, "shutdown"),
    ]);

    // ---- Attention routing (AUD-1.4 slice 7) ----
    // The instance default targets the instance; an override targets its integration (name as a snapshot, owning context as the
    // related reference). A condition key is recorded as a closed enum: overrides accept keys nothing validates, so any key other
    // than the one the platform defines is recorded as "other" and a caller's text can never reach a field. A change is described
    // by the names of the fields that differ. Team ids, durations, flag values, recipients and addresses have no field.

    /// <summary>The routing fields an update can change (named, never valued).</summary>
    internal static readonly string[] AttentionRoutingChangeFields =
    [
        "enabled",
        "sendResolvedNotification",
        "suppressRemindersWhenAcknowledged",
        "suppressEscalationWhenAcknowledged",
        "escalationAfter",
        "reminderInterval",
        "primaryTeam",
        "escalationTeam",
    ];

    private static AuditFieldDefinition AttentionConditionKeyField() =>
        EnumField("conditionKey", true, "expected_run_missing", "other");

    internal static readonly AuditDetailSchema AttentionRoutingDefaultUpdated = new(
    [
        AttentionConditionKeyField(),
        FieldKeysField("changedFields", required: true),
    ]);

    internal static readonly AuditDetailSchema AttentionRoutingOverrideCreated = new(
    [
        NameField("name"),
        AttentionConditionKeyField(),
    ]);

    internal static readonly AuditDetailSchema AttentionRoutingOverrideUpdated = new(
    [
        NameField("name"),
        AttentionConditionKeyField(),
        FieldKeysField("changedFields", required: true),
    ]);

    internal static readonly AuditDetailSchema AttentionRoutingOverrideCleared = new(
    [
        NameField("name"),
        AttentionConditionKeyField(),
    ]);

    // ---- Attention incident acknowledgement (SEC-2.1) ----
    // The row names the integration (its name as a snapshot), the incident and its condition, when the incident started and
    // whether the operator gave a note. The note's text, the recipients and any notification content are never fields.

    internal static readonly AuditDetailSchema AttentionIncidentAcknowledged = new(
    [
        NameField("name"),
        IdField("incidentId"),
        AttentionConditionKeyField(),
        new AuditFieldDefinition("incidentStartedAt", AuditFieldKind.Timestamp, Required: true),
        FlagField("noteProvided"),
    ]);

    // ---- Module lifecycle (AUD-1.4 slice 7) ----
    // The module is the target and the related module reference. A row records the persisted installation state before and
    // after and the restart effect the transition reports; it never claims a module is active in any runtime process. A blocked
    // attempt names the blocker codes only, never their messages.

    private static readonly string[] ModuleStates = ["discovered", "installed_disabled", "installed_enabled"];

    internal static readonly AuditDetailSchema ModuleLifecycleChanged = new(
    [
        IdField("version"),
        IdField("previousVersion", required: false),
        EnumField("previousState", true, ModuleStates),
        EnumField("state", true, ModuleStates),
        EnumField("restartEffect", true, "immediate", "partial_until_restart", "requires_restart"),
        FlagField("implicitInstall", required: false),
    ]);

    internal static readonly AuditDetailSchema ModuleLifecycleBlocked = new(
    [
        EnumField("state", true, ModuleStates),
        new AuditFieldDefinition("blockers", AuditFieldKind.IdList, Required: true, MaxItems: 16),
    ]);
}
