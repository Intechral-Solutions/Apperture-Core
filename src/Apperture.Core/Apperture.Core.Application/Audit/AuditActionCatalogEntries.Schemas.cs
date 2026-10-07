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
}
