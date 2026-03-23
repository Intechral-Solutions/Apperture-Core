namespace Apperture.ControlPlane.Security.Models;

/// <summary>
/// Operator UI capabilities derived from principal permissions. Epic 34.
/// Epic 40: CanViewGovernance (user.read), CanAssignPermissions (permission.assign).
/// Epic 41: CanAssignRoles (role.assign).
/// </summary>
public sealed record OperatorCapabilitiesDto(
    bool CanManageSchedules,
    bool CanManageRecovery,
    bool CanViewAudit,
    bool CanViewRuntimeHealth,
    bool CanManageModules,
    bool CanViewGovernance = false,
    bool CanAssignPermissions = false,
    bool CanAssignRoles = false);
