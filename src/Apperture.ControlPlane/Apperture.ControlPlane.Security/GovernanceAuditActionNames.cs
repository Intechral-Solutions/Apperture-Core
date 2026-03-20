namespace Apperture.ControlPlane.Security;

/// <summary>
/// Canonical audit action names for privileged governance operations.
/// </summary>
public static class GovernanceAuditActionNames
{
    public const string RoleAssigned = "governance.role.assignment.created";
    public const string RoleRemoved = "governance.role.assignment.removed";
    public const string PermissionAssignmentUpdated = "governance.permission.assignment.updated";
    public const string BreakGlassAdminOperation = "governance.break_glass.admin_operation";

    // Runtime enrollment (operator control surface)
    public const string RuntimeEnrollmentInvitationCreated = "governance.runtime.enrollment.invitation.created";
}

