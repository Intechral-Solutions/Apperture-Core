namespace Apperture.ControlPlane.Security;

/// <summary>
/// Configuration for local username/password authentication.
/// Bind from section "LocalAuth" or "Authentication:Local".
/// </summary>
public sealed class LocalAuthOptions
{
    public const string SectionName = "LocalAuth";

    /// <summary>
    /// When true, local login (POST /api/auth/local/login) is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Minimum password length for new passwords (optional; for future policy).
    /// </summary>
    public int MinPasswordLength { get; set; } = 8;
}
