namespace Apperture.ControlPlane.Security.Models;

/// <summary>
/// Operator UI capabilities derived from principal permissions. Epic 34.
/// </summary>
public sealed record OperatorCapabilitiesDto(
    bool CanManageSchedules,
    bool CanManageRecovery,
    bool CanViewAudit,
    bool CanViewRuntimeHealth,
    bool CanManageModules);
