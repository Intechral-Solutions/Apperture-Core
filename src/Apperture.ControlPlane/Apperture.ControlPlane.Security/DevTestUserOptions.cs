namespace Apperture.ControlPlane.Security;

/// <summary>
/// Configuration for the dev test user bootstrap. Only used in Development.
/// Bind from section "DevTestUser".
/// </summary>
public sealed class DevTestUserOptions
{
    public const string SectionName = "DevTestUser";

    /// <summary>
    /// When true, ensures the configured dev test user exists with platform-admin role at startup.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Local username (normalized: trim, lowercase). Default "tenny".
    /// </summary>
    public string Username { get; set; } = "tenny";

    /// <summary>
    /// Display name for the user. Default "Tenny (Dev)".
    /// </summary>
    public string DisplayName { get; set; } = "Tenny (Dev)";

    /// <summary>
    /// Password for local credential. Required when Enabled. Use a dev-only value.
    /// </summary>
    public string? Password { get; set; }
}
