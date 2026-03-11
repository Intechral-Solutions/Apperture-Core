namespace Apperture.Core.Application;

/// <summary>
/// Constants for first-admin bootstrap. Used by bootstrap service and migration seed.
/// </summary>
public static class BootstrapConstants
{
    /// <summary>
    /// Role id for the platform administrator role (seeded by migration; used for first-admin assignment).
    /// </summary>
    public const string PlatformAdminRoleId = "role-platform-administrator";
}
