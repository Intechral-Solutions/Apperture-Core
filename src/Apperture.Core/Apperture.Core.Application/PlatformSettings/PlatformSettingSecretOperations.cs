namespace Apperture.Core.Application.PlatformSettings;

/// <summary>Explicit secret mutation verbs for sensitive platform settings (never overload empty string).</summary>
public static class PlatformSettingSecretOperations
{
    public const string Replace = "replace";

    public const string Clear = "clear";

    public static bool IsKnown(string? operation) =>
        string.Equals(operation, Replace, StringComparison.OrdinalIgnoreCase)
        || string.Equals(operation, Clear, StringComparison.OrdinalIgnoreCase);

    public static string? Normalize(string? operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
            return null;

        if (string.Equals(operation, Replace, StringComparison.OrdinalIgnoreCase))
            return Replace;

        if (string.Equals(operation, Clear, StringComparison.OrdinalIgnoreCase))
            return Clear;

        return null;
    }
}
