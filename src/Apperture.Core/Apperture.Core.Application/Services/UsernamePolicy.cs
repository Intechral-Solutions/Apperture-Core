using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// The one username rule for every user-creation path, generation, rename and the emergency CLI.
/// </summary>
/// <remarks>
/// The format describes what may be <b>assigned</b>, not what every stored value looks like. Existing
/// local usernames that predate this policy (for example <c>jane@contoso.com</c>) are kept verbatim and
/// stay valid identities. Code must not assume a stored username matches <see cref="IsValidFormat"/>,
/// is free of reserved words, or is safe as a URL path segment.
/// </remarks>
public static class UsernamePolicy
{
    public const int MinLength = UsernameRules.MinLength;
    public const int MaxLength = UsernameRules.MaxLength;

    /// <summary>Longest generated base, leaving room for a <c>-xxxxxx</c> collision suffix.</summary>
    public const int GeneratedBaseMaxLength = 56;

    /// <summary>Canonical Recovery Access username. Reserved: only provisioning and canonicalization assign it.</summary>
    public const string RecoveryAccessUsername = UsernameRules.RecoveryAccessUsername;

    /// <summary>Last-resort generation source when no directory attribute yields a usable name.</summary>
    public const string FallbackGeneratedBase = "user";

    private const int NumberedSuffixMax = 20;
    private const int RandomSuffixAttempts = 5;
    private const int RandomSuffixLength = 6;
    private const string RandomSuffixAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Names no new assignment may take (see <see cref="UsernameRules.ReservedNames"/>, the one list, which the browser
    /// client shares).
    /// </summary>
    public static IReadOnlySet<string> ReservedNames => UsernameRules.ReservedNames;

    /// <summary>Trim, then lower-case invariant. The result is the canonical stored form.</summary>
    public static string Normalize(string username) => UsernameRules.Normalize(username);

    public static bool IsCanonical(string username) =>
        !string.IsNullOrEmpty(username) && string.Equals(Normalize(username), username, StringComparison.Ordinal);

    public static bool IsReserved(string normalizedUsername) => UsernameRules.IsReserved(normalizedUsername);

    /// <summary>True for the 24-lowercase-hex shape of a user id, which a username may never take.</summary>
    public static bool IsUserIdShaped(string value) => UsernameRules.IsUserIdShaped(value);

    /// <summary>
    /// Format rules for a new assignment, without the reserved-name check: 3 to 64 characters of
    /// <c>a-z 0-9 . - _</c>, starting with a letter, ending with a letter or digit, no two separators
    /// in a row, and not shaped like a user id. See <see cref="UsernameRules.IsValidFormat"/>.
    /// </summary>
    public static bool IsValidFormat(string normalizedUsername) => UsernameRules.IsValidFormat(normalizedUsername);

    /// <summary>Classifies an already-normalized value as a candidate for an ordinary new assignment.</summary>
    public static UsernameAssignmentCheck CheckNewAssignment(string normalizedUsername)
    {
        if (!IsValidFormat(normalizedUsername))
            return UsernameAssignmentCheck.Invalid;
        return IsReserved(normalizedUsername) ? UsernameAssignmentCheck.Reserved : UsernameAssignmentCheck.Valid;
    }

    /// <summary>
    /// Normalizes <paramref name="requested"/> and requires it to satisfy the policy for an ordinary new
    /// assignment (creation, rename, reclaim). Throws <see cref="UsernameException"/> otherwise.
    /// </summary>
    public static string RequireNewAssignment(string requested)
    {
        var normalized = Normalize(requested ?? "");
        switch (CheckNewAssignment(normalized))
        {
            case UsernameAssignmentCheck.Valid:
                return normalized;
            case UsernameAssignmentCheck.Reserved:
                throw new UsernameException(UsernameErrorCodes.Reserved, "That username is reserved.");
            default:
                throw new UsernameException(UsernameErrorCodes.Invalid, UsernameRules.FormatGuidance);
        }
    }

    /// <summary>
    /// Generates the base username for a user who has no chosen name (Entra-only users): the UPN local
    /// part unless the UPN is a guest <c>#EXT#</c> name, then the mail local part, then the display name,
    /// then <see cref="FallbackGeneratedBase"/>. Directory object ids are never used.
    /// </summary>
    public static string GenerateBase(UsernameGenerationSources sources)
    {
        var upn = sources.UserPrincipalName;
        if (!string.IsNullOrWhiteSpace(upn) && upn.Contains("#EXT#", StringComparison.OrdinalIgnoreCase))
            upn = null;

        return SanitizeCandidate(LocalPart(upn), fromDisplayName: false)
            ?? SanitizeCandidate(LocalPart(sources.Mail), fromDisplayName: false)
            ?? SanitizeCandidate(sources.DisplayName, fromDisplayName: true)
            ?? FallbackGeneratedBase;
    }

    /// <summary>
    /// Turns one raw source value into a usable base, or null when nothing usable remains (too short,
    /// reserved, user-id shaped or otherwise invalid), so the caller moves to the next source.
    /// </summary>
    public static string? SanitizeCandidate(string? raw, bool fromDisplayName)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // 1. Unicode compatibility decomposition, dropping combining marks: "José" -> "Jose".
        var decomposed = raw.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingWhitespace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            // 3. Display names only: runs of whitespace become one '.'.
            if (char.IsWhiteSpace(ch))
            {
                pendingWhitespace = fromDisplayName;
                continue;
            }

            if (pendingWhitespace)
            {
                builder.Append('.');
                pendingWhitespace = false;
            }

            // 2. Lower-case invariant. 4. Drop everything outside a-z 0-9 . - _
            var lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9' || IsSeparator(lower))
                builder.Append(lower);
        }

        // 5. Collapse runs of separators to the first separator of the run.
        var collapsed = new StringBuilder(builder.Length);
        for (var i = 0; i < builder.Length; i++)
        {
            if (IsSeparator(builder[i]) && collapsed.Length > 0 && IsSeparator(collapsed[^1]))
                continue;
            collapsed.Append(builder[i]);
        }

        // 6. Trim separators and leading digits from the start, separators from the end.
        var value = collapsed.ToString();
        var start = 0;
        while (start < value.Length && (IsSeparator(value[start]) || char.IsAsciiDigit(value[start])))
            start++;
        value = value[start..].TrimEnd('.', '-', '_');

        // 7. Truncate and re-trim trailing separators.
        if (value.Length > GeneratedBaseMaxLength)
            value = value[..GeneratedBaseMaxLength].TrimEnd('.', '-', '_');

        // 8. Reject anything that cannot be assigned.
        return CheckNewAssignment(value) == UsernameAssignmentCheck.Valid ? value : null;
    }

    /// <summary>
    /// Candidates in order: the base, <c>base-2</c> to <c>base-20</c>, then five <c>base-</c> plus six
    /// random characters. The database ledger decides which one is free.
    /// </summary>
    public static IEnumerable<string> CollisionCandidates(string baseName, Func<string>? randomSuffix = null)
    {
        yield return baseName;
        for (var n = 2; n <= NumberedSuffixMax; n++)
            yield return $"{baseName}-{n}";

        randomSuffix ??= () => RandomNumberGenerator.GetString(RandomSuffixAlphabet, RandomSuffixLength);
        for (var attempt = 0; attempt < RandomSuffixAttempts; attempt++)
            yield return $"{baseName}-{randomSuffix()}";
    }

    private static string? LocalPart(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;
        var at = address.LastIndexOf('@');
        return at < 0 ? address : address[..at];
    }

    private static bool IsSeparator(char c) => UsernameRules.IsSeparator(c);
}

/// <summary>Result of checking a normalized value against the policy for an ordinary new assignment.</summary>
public enum UsernameAssignmentCheck
{
    Valid,
    Invalid,
    Reserved,
}

/// <summary>Directory attributes a generated username may be derived from. Never an object id.</summary>
public sealed record UsernameGenerationSources(string? UserPrincipalName, string? Mail, string? DisplayName)
{
    public static UsernameGenerationSources None { get; } = new(null, null, null);
}

/// <summary>Expected failure of a username assignment, rename or reclaim.</summary>
public sealed class UsernameException : Exception
{
    public UsernameException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable username error codes for APIs and clients.</summary>
public static class UsernameErrorCodes
{
    /// <summary>The name is, or was, owned by another user.</summary>
    public const string Taken = "username_taken";

    public const string Invalid = "username_invalid";
    public const string Reserved = "username_reserved";

    /// <summary>A Local identity link must use the user's username as its external id.</summary>
    public const string Mismatch = "username_mismatch";

    public const string GenerationExhausted = "username_generation_exhausted";

    /// <summary>The user's username changed underneath this operation.</summary>
    public const string ConcurrentChange = "username_concurrent_change";
}

/// <summary>Bounded <c>UsernameLedger.RetiredReason</c> vocabulary.</summary>
public static class UsernameRetiredReasons
{
    public const string Rename = "rename";
    public const string Canonicalization = "canonicalization";
    public const string BackfillPrevious = "backfill_previous";
}
