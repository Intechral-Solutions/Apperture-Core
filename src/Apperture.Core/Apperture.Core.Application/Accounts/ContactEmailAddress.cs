namespace Apperture.Core.Application.Accounts;

/// <summary>
/// Normalizes Apperture contact addresses. Contact email is never an authentication identity key.
/// </summary>
public static class ContactEmailAddress
{
    public const int MaxLength = 512;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            return null;

        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
            return null;

        if (trimmed.Any(char.IsWhiteSpace))
            return null;

        return trimmed.ToLowerInvariant();
    }

    public static bool IsValid(string? value) => Normalize(value) is not null;
}
