namespace Apperture.Management.Ui.Shared;

/// <summary>User-selectable appearance. Persisted in browser storage under <see cref="StorageKey"/>.</summary>
public enum ThemePreference
{
    Light,
    Dark,
    System
}

/// <summary>Parses persisted theme strings for the UI theme service and tests.</summary>
public static class ThemePreferenceParser
{
    public const string StorageKey = "apperture.theme";

    public static ThemePreference ParseOrDefault(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return ThemePreference.System;
        return Enum.TryParse<ThemePreference>(stored.Trim(), ignoreCase: true, out var p)
            ? p
            : ThemePreference.System;
    }

    public static string ToStorageValue(ThemePreference preference) => preference.ToString();
}
