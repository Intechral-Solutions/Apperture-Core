namespace Apperture.Management.Ui.Shared;

/// <summary>Where the effective appearance came from.</summary>
public enum AppearancePreferenceSource
{
    /// <summary>Nothing stored anywhere; the platform default applied.</summary>
    PlatformDefault = 0,

    /// <summary>Read from this browser's local storage (used before sign-in).</summary>
    Browser = 1,

    /// <summary>Read from the signed-in operator's stored account preference.</summary>
    AccountPreference = 2
}

/// <summary>
/// Resolves which appearance wins.
/// </summary>
/// <remarks>
/// Precedence, highest first:
/// <list type="number">
///   <item>the signed-in operator's stored account preference;</item>
///   <item>this browser's local preference, which is what an unauthenticated or still-loading
///     session has to go on;</item>
///   <item>the platform default, <see cref="ThemePreference.System"/>.</item>
/// </list>
/// The operating system setting is not a separate tier: it is what <see cref="ThemePreference.System"/>
/// resolves to at paint time, so it applies whenever the winning preference is System.
///
/// The account preference is mirrored into local storage when it loads. That is what lets the
/// pre-WebAssembly boot script in the host page pick the right theme on the next visit, so a signed-in
/// operator does not see a flash of the wrong theme before the session is known.
/// </remarks>
public static class AppearancePreferenceResolution
{
    public static ThemePreference PlatformDefault => ThemePreference.System;

    public static (ThemePreference Preference, AppearancePreferenceSource Source) Resolve(
        string? accountPreference,
        string? browserPreference)
    {
        if (TryParseExact(accountPreference, out var fromAccount))
            return (fromAccount, AppearancePreferenceSource.AccountPreference);

        if (TryParseExact(browserPreference, out var fromBrowser))
            return (fromBrowser, AppearancePreferenceSource.Browser);

        return (PlatformDefault, AppearancePreferenceSource.PlatformDefault);
    }

    /// <summary>
    /// Strict parse. Unlike <see cref="ThemePreferenceParser.ParseOrDefault"/> this reports absence
    /// instead of substituting System, which is what lets a missing value fall through to the next tier.
    /// </summary>
    public static bool TryParseExact(string? value, out ThemePreference preference)
    {
        preference = PlatformDefault;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out preference)
            && Enum.IsDefined(preference);
    }

    public static string Describe(AppearancePreferenceSource source) => source switch
    {
        AppearancePreferenceSource.AccountPreference => "Saved to your account",
        AppearancePreferenceSource.Browser => "Saved in this browser",
        _ => "Platform default"
    };
}
