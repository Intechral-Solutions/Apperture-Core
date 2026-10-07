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
}
