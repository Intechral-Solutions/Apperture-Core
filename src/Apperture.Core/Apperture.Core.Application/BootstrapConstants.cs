namespace Apperture.Core.Application;

/// <summary>
/// Constants for first-admin and platform bootstrap. Used by migration seed, role repair, recovery, and
/// <c>BootstrapPlatformAdmin</c> configuration-driven assignment.
/// </summary>
public static class BootstrapConstants
{
    /// <summary>
    /// Role id for the system-managed Platform Administrator role (<c>IsSystemRole</c> in persistence).
    /// Seeded by EF migration; permissions are repaired at startup by <c>PlatformAdminRoleRepairService</c>.
    /// </summary>
    public const string PlatformAdminRoleId = "role-platform-administrator";
}
