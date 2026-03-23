namespace Apperture.ControlPlane.Security;

/// <summary>High-level description of the effective management UI sign-in experience for operators.</summary>
public enum EffectiveAuthExperienceKind
{
    /// <summary>No usable sign-in path (misconfiguration).</summary>
    NoViableSignIn,

    /// <summary>Only local username/password (Entra off or invalid).</summary>
    LocalOnly,

    /// <summary>Entra enabled and misconfigured; local may still work.</summary>
    EntraMisconfiguredLocalAvailable,

    /// <summary>Entra works; local hidden from primary chooser but available at /login/local.</summary>
    EntraPrimaryLocalFallback,

    /// <summary>Both Entra and local appear on the primary sign-in page.</summary>
    DualVisible
}
