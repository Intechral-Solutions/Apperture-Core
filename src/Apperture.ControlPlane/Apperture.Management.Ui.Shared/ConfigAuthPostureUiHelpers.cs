using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Proactive authentication-posture summary for the Config landing page. Restores the retired
/// Dashboard <c>auth-warnings</c> card's "no viable sign-in path" signal on a current surface
/// (NAV-1 stabilization, independent-review F-01) without a new card framework.
/// </summary>
public static class ConfigAuthPostureUiHelpers
{
    public const string NoViableSignInExperience = "NoViableSignIn";

    public const string NoViableSignInMessage =
        "No viable sign-in path — enable local auth or fix Entra.";

    /// <summary>
    /// Resolves the warning banner text for Config overview, or null when there is nothing to
    /// surface (no overview available -- e.g. the operator lacks the capability and the API
    /// returned null -- or no warnings/no-viable-signin condition).
    /// </summary>
    public static string? ResolveWarning(ConfigAuthOverviewDto? overview)
    {
        if (overview is null)
            return null;

        if (string.Equals(overview.EffectiveExperience, NoViableSignInExperience, StringComparison.Ordinal))
            return NoViableSignInMessage;

        if (overview.Warnings.Count == 1)
            return overview.Warnings[0];

        if (overview.Warnings.Count > 1)
            return $"{overview.Warnings.Count} authentication warning(s).";

        return null;
    }
}
