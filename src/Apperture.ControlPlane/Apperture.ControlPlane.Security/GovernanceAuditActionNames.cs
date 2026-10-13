namespace Apperture.ControlPlane.Security;

/// <summary>
/// Canonical audit action names for privileged governance operations.
/// </summary>
public static class GovernanceAuditActionNames
{
    public const string RoleAssigned = "governance.role.assignment.created";
    public const string RoleRemoved = "governance.role.assignment.removed";
    public const string PermissionAssignmentUpdated = "governance.permission.assignment.updated";
    public const string RoleCreated = "governance.role.created";
    public const string RoleMetadataUpdated = "governance.role.metadata.updated";
    public const string RoleDeleted = "governance.role.deleted";
    public const string RoleMutationRejected = "governance.role.mutation_rejected";
    /// <summary>Historical only: nothing writes this action any more. Rows already written keep it (catalog entry marked retired).</summary>
    public const string BreakGlassAdminOperation = "governance.break_glass.admin_operation";
    public const string UserUsernameChanged = Apperture.Core.Application.Audit.AuditActionNames.UsernameChanged;

    // Runtime enrollment (operator control surface)
    public const string RuntimeEnrollmentInvitationCreated = "governance.runtime.enrollment.invitation.created";

    // Runtime external control (operator control surface)
    public const string RuntimeShutdownRequestCreated = Apperture.Core.Application.Audit.AuditActionNames.RuntimeShutdownRequestCreated;

    /// <summary>Epic 34: audit when audit.read is denied due to permission.</summary>
    public const string AuditReadDenied = "audit.read.denied";
}

