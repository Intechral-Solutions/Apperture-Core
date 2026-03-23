namespace Apperture.ControlPlane.Security;

/// <summary>Maps merged <see cref="AuthSignInOptionsSnapshot"/> to an operator-facing experience label.</summary>
public static class AuthSignInEffectiveExperience
{
    public static EffectiveAuthExperienceKind Resolve(AuthSignInOptionsSnapshot s)
    {
        if (!s.LocalSignInEnabled && !s.EntraSignInEnabled)
            return EffectiveAuthExperienceKind.NoViableSignIn;

        if (s.EntraMisconfigured && s.LocalSignInEnabled)
            return EffectiveAuthExperienceKind.EntraMisconfiguredLocalAvailable;

        if (s.LocalSignInEnabled && !s.EntraSignInEnabled)
            return EffectiveAuthExperienceKind.LocalOnly;

        // Entra usable from here
        if (s.EntraSignInEnabled && s.LocalSignInEnabled && !s.ShowLocalSignInInPrimaryChooser)
            return EffectiveAuthExperienceKind.EntraPrimaryLocalFallback;

        if (s.EntraSignInEnabled && s.LocalSignInEnabled && s.ShowLocalSignInInPrimaryChooser)
            return EffectiveAuthExperienceKind.DualVisible;

        // Entra only, local disabled
        if (s.EntraSignInEnabled && !s.LocalSignInEnabled)
            return EffectiveAuthExperienceKind.EntraOnlyPrimary;

        return EffectiveAuthExperienceKind.NoViableSignIn;
    }

    public static string Describe(EffectiveAuthExperienceKind k) => k switch
    {
        EffectiveAuthExperienceKind.NoViableSignIn =>
            "No valid sign-in path: enable local auth or fix Entra configuration.",
        EffectiveAuthExperienceKind.LocalOnly =>
            "Users sign in with local username and password only.",
        EffectiveAuthExperienceKind.EntraMisconfiguredLocalAvailable =>
            "Entra is enabled but misconfigured; users should use local sign-in until Entra is fixed.",
        EffectiveAuthExperienceKind.EntraPrimaryLocalFallback =>
            "Primary sign-in emphasizes Microsoft (Entra); local sign-in remains available at /login/local for recovery.",
        EffectiveAuthExperienceKind.EntraOnlyPrimary =>
            "Only Microsoft (Entra) sign-in is available because local auth is disabled in deployment.",
        EffectiveAuthExperienceKind.DualVisible =>
            "Both Microsoft (Entra) and local sign-in appear on the primary sign-in page (ordering follows policy).",
        _ => k.ToString()
    };
}
