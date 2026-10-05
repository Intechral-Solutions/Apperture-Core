namespace Apperture.Core.Domain.Identity;

/// <summary>
/// The pure rules for a new username assignment: normalization, format and reserved names. They live in the domain so
/// the browser client can check a typed name with exactly the rule the server enforces; the server's
/// <c>UsernamePolicy</c> delegates here and adds generation and the ledger-backed rules.
/// </summary>
/// <remarks>
/// The format describes what may be <b>assigned</b>, not what every stored value looks like. Existing local
/// usernames that predate it (for example <c>jane@contoso.com</c>) stay valid identities, so callers must not assume a
/// stored username satisfies <see cref="IsValidFormat"/>.
/// </remarks>
public static class UsernameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 64;

    /// <summary>Canonical Recovery Access username. Reserved: only provisioning and canonicalization assign it.</summary>
    public const string RecoveryAccessUsername = "breakglass";

    /// <summary>What a valid new username looks like. The same text is returned by the API when a name is refused.</summary>
    public const string FormatGuidance =
        "A username must be 3 to 64 characters: lowercase letters, digits, '.', '-' or '_', starting with a letter and ending with a letter or digit, with no two separators in a row.";

    /// <summary>Shown (and returned by the API) when a typed name is one of <see cref="ReservedNames"/>.</summary>
    public const string ReservedMessage = "That username is reserved.";

    /// <summary>
    /// Names no new assignment may take: Recovery Access, plus the literal actor values persisted in
    /// history, so a person is never confused with a non-human actor.
    /// </summary>
    public static IReadOnlySet<string> ReservedNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RecoveryAccessUsername,
        "system",
        "anonymous",
        "recovery",
        "emergency-cli",
        "worker",
    };

    /// <summary>Trim, then lower-case invariant. The result is the canonical stored form.</summary>
    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    public static bool IsReserved(string normalizedUsername) => ReservedNames.Contains(normalizedUsername);

    public static bool IsSeparator(char c) => c is '.' or '-' or '_';

    /// <summary>
    /// How a form field should present a typed username as the operator types: pristine while blank, valid when the
    /// normalized name may be assigned, otherwise invalid with the same message the server would return. Uniqueness is
    /// never known here; the server confirms it on submit.
    /// </summary>
    public static FieldValidationPresentation ToFieldValidation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return FieldValidationPresentation.Pristine;

        var normalized = Normalize(raw);
        if (!IsValidFormat(normalized))
            return FieldValidationPresentation.Invalid(FormatGuidance);

        return IsReserved(normalized) ? FieldValidationPresentation.Invalid(ReservedMessage) : FieldValidationPresentation.Valid;
    }

    /// <summary>True for the 24-lowercase-hex shape of a user id, which a username may never take.</summary>
    public static bool IsUserIdShaped(string value)
    {
        if (value.Length != 24)
            return false;
        foreach (var c in value)
        {
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Format rules for a new assignment, without the reserved-name check: 3 to 64 characters of
    /// <c>a-z 0-9 . - _</c>, starting with a letter, ending with a letter or digit, no two separators
    /// in a row, and not shaped like a user id.
    /// </summary>
    public static bool IsValidFormat(string normalizedUsername)
    {
        var value = normalizedUsername;
        if (value.Length is < MinLength or > MaxLength)
            return false;
        if (value[0] is not (>= 'a' and <= 'z'))
            return false;
        if (IsSeparator(value[^1]))
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (IsSeparator(c))
            {
                if (IsSeparator(value[i - 1]))
                    return false;
            }
            else if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
            {
                return false;
            }
        }

        return !IsUserIdShaped(value);
    }
}
