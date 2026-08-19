namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Validation for the platform-owned <see cref="User.DisplayName"/>. Display name is stored by
/// Apperture and is never overwritten from an identity provider after account creation, so an
/// operator edit is authoritative.
/// </summary>
public static class DisplayNameRules
{
    public const int MinLength = 1;
    public const int MaxLength = 128;

    /// <summary>Collapses surrounding whitespace. Interior spacing is left as the operator typed it.</summary>
    public static string Normalize(string? value) => value?.Trim() ?? "";

    public static bool TryNormalize(string? value, out string normalized, out string? error)
    {
        normalized = Normalize(value);
        if (normalized.Length < MinLength)
        {
            error = "Display name is required.";
            return false;
        }

        if (normalized.Length > MaxLength)
        {
            error = $"Display name must be {MaxLength} characters or fewer.";
            return false;
        }

        if (normalized.Any(char.IsControl))
        {
            error = "Display name cannot contain control characters.";
            return false;
        }

        error = null;
        return true;
    }
}
