namespace Apperture.Core.Application.Services;

/// <summary>
/// The non-human actor values the platform writes into actor columns and audit rows. A human actor is always a
/// user id; these literals are the only other values a current write produces. Historical actor normalization and
/// audit actor classification read non-human actors from here, so a literal is never mistaken for a person.
/// </summary>
/// <remarks>
/// Exact literals, plus the <see cref="SystemPrefix"/> family (<c>system:scheduler</c>, <c>system:module-lifecycle</c>,
/// <c>system:unknown</c>, and later subtypes). A recorded worker or host id is data, not a literal, and is not
/// classified here. Matching is ordinal: the platform writes these values in exactly this form.
/// </remarks>
public static class ActorLiterals
{
    /// <summary>An unauthenticated request, such as a failed sign-in.</summary>
    public const string Anonymous = "anonymous";

    /// <summary>The platform itself, with no more specific subtype.</summary>
    public const string System = "system";

    /// <summary>Bootstrap platform-access recovery.</summary>
    public const string Recovery = "recovery";

    /// <summary>The emergency access command-line tool.</summary>
    public const string EmergencyCli = "emergency-cli";

    /// <summary>The Worker Host processing durable start requests.</summary>
    public const string SystemWorker = "system-worker";

    /// <summary>A platform subsystem: <c>system:</c> followed by a non-empty subtype.</summary>
    public const string SystemPrefix = "system:";

    /// <summary>The exact (non-prefix) literals.</summary>
    public static IReadOnlySet<string> ExactLiterals { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Anonymous,
        System,
        Recovery,
        EmergencyCli,
        SystemWorker,
    };

    /// <summary>
    /// True when <paramref name="value"/> is a non-human actor literal: one of <see cref="ExactLiterals"/>, or
    /// <see cref="SystemPrefix"/> followed by a subtype. Null, empty, user ids and usernames are not literals.
    /// </summary>
    public static bool IsNonHumanLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        if (ExactLiterals.Contains(value))
            return true;
        return value.Length > SystemPrefix.Length && value.StartsWith(SystemPrefix, StringComparison.Ordinal);
    }
}
