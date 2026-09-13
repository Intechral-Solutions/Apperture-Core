namespace Apperture.Core.Domain.Identity;

/// <summary>Operator appearance choice. <see cref="System"/> follows the operating system setting.</summary>
public enum AppearancePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
/// Clock convention for operator-facing timestamps. <see cref="System"/> keeps the platform default
/// 24-hour rendering.
/// </summary>
public enum TimeFormatPreference
{
    System = 0,
    Hour12 = 1,
    Hour24 = 2
}

/// <summary>
/// Typed personal preferences owned by the signed-in operator. Deliberately closed: values a user
/// cannot actually change, and settings an administrator owns, do not belong here.
/// </summary>
/// <remarks>
/// The operator display timezone is not part of this record. It already lives on
/// <see cref="User.DisplayTimeZoneId"/> and is read by the shell's timestamp rendering, so AUP-1
/// reuses that column rather than introducing a second timezone preference.
/// </remarks>
public sealed record UserPreferences(
    AppearancePreference Appearance,
    TimeFormatPreference TimeFormat)
{
    /// <summary>Applied whenever a user has no stored preference row.</summary>
    public static UserPreferences Default { get; } =
        new(AppearancePreference.System, TimeFormatPreference.System);

    /// <summary>
    /// Rebuilds preferences from persisted strings, silently falling back to defaults for values
    /// written by an older or corrupted row.
    /// </summary>
    public static UserPreferences FromStorage(string? appearance, string? timeFormat) =>
        new(
            ParseAppearanceOrDefault(appearance),
            ParseTimeFormatOrDefault(timeFormat));

    public static AppearancePreference ParseAppearanceOrDefault(string? value) =>
        TryParseAppearance(value, out var parsed) ? parsed : AppearancePreference.System;

    public static TimeFormatPreference ParseTimeFormatOrDefault(string? value) =>
        TryParseTimeFormat(value, out var parsed) ? parsed : TimeFormatPreference.System;

    /// <summary>Strict parse for request payloads. Unknown values must be rejected, not defaulted.</summary>
    public static bool TryParseAppearance(string? value, out AppearancePreference appearance)
    {
        appearance = AppearancePreference.System;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out appearance)
            && Enum.IsDefined(appearance);
    }

    /// <summary>Strict parse for request payloads. Unknown values must be rejected, not defaulted.</summary>
    public static bool TryParseTimeFormat(string? value, out TimeFormatPreference timeFormat)
    {
        timeFormat = TimeFormatPreference.System;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out timeFormat)
            && Enum.IsDefined(timeFormat);
    }
}
