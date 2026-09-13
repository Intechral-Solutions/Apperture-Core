using Zxcvbn;

namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Canonical password evaluation used by WASM (real-time UX) and by server/CLI (authoritative
/// enforcement). Guessability comes from the zxcvbn-ts-compatible <c>Zxcvbn</c> library; acceptance
/// comes from <see cref="PasswordPolicyProfile"/>.
/// </summary>
public sealed class PasswordPolicyEvaluator : IPasswordPolicyEvaluator
{
    public static PasswordPolicyEvaluator Instance { get; } = new();

    private static readonly string[] PlatformTerms =
    [
        "apperture", "aperture", "breakglass", "recoveryaccess", "emergencyaccess",
        "recovery", "emergency"
    ];

    // Built once per process. Dictionaries stay in this analyzer; userInputs are a
    // lightweight per-call overlay and do not reload the bundled ranked lists.
    private static readonly ZxcvbnAnalyzer Analyzer = CreateAnalyzer();

    public PasswordEvaluationResult Evaluate(
        string? password,
        PasswordEvaluationContext? context = null,
        PasswordPolicyProfile? profile = null)
    {
        profile ??= PasswordPolicyProfile.Ordinary;
        context ??= PasswordEvaluationContext.Platform;
        var issues = new List<string>();
        var suggestions = new List<string>();
        var checks = new List<PasswordPolicyCheck>();

        if (string.IsNullOrEmpty(password))
        {
            issues.Add("Enter a password.");
            suggestions.Add($"Use a long passphrase of at least {profile.MinimumLength} characters.");
            checks.Add(new("length", false, $"Use at least {profile.MinimumLength} characters"));
            checks.Add(new("strength", false, "Choose a longer or less predictable passphrase"));
            if (profile.BlockContextualPasswords)
                checks.Add(new("context", false, "Does not contain account details"));
            return new PasswordEvaluationResult(false, PasswordStrengthLevel.VeryWeak, 0, issues, suggestions, checks);
        }

        if (password.Length > profile.MaximumLength)
        {
            issues.Add($"Password must be at most {profile.MaximumLength} characters.");
            checks.Add(new("length", false, $"Use at most {profile.MaximumLength} characters."));
            return new PasswordEvaluationResult(false, PasswordStrengthLevel.VeryWeak, 0, issues, suggestions, checks);
        }

        var lengthOk = password.Length >= profile.MinimumLength;
        checks.Add(lengthOk
            ? new("length", true, $"Use at least {profile.MinimumLength} characters")
            : new("length", false, $"Use at least {profile.MinimumLength} characters"));
        if (!lengthOk)
        {
            issues.Add($"Use at least {profile.MinimumLength} characters.");
            suggestions.Add("A short phrase of several words is easier to remember than a short complex password.");
        }

        var contextTerms = EnumerateContextTerms(context, profile).ToArray();
        var zx = Analyzer.Evaluate(password, profile.BlockContextualPasswords ? contextTerms : Array.Empty<string>());
        var score = Math.Clamp(zx.Score, 0, 4);
        var strength = (PasswordStrengthLevel)score;
        var strengthOk = score >= profile.MinimumStrengthScore;
        checks.Add(strengthOk
            ? new("strength", true, "Strength is sufficient")
            : new("strength", false, "Choose a longer or less predictable passphrase"));
        if (!strengthOk && lengthOk)
        {
            issues.Add("Choose a longer or less predictable passphrase.");
            if (!string.IsNullOrWhiteSpace(zx.Feedback.Warning))
                suggestions.Add(zx.Feedback.Warning);
            foreach (var suggestion in zx.Feedback.Suggestions)
            {
                if (!string.IsNullOrWhiteSpace(suggestion))
                    suggestions.Add(suggestion);
            }

            if (suggestions.Count == 0)
                suggestions.Add("Four or more unrelated words is usually enough.");
        }

        var common = false;
        if (profile.BlockCommonPasswords)
        {
            var folded = Fold(password);
            common = CommonPasswords.Contains(folded)
                || CommonPasswords.Contains(folded.Replace(" ", "", StringComparison.Ordinal));
            if (common)
            {
                issues.Add("That password is too common.");
                suggestions.Add("Choose something that is not a well-known password or phrase.");
            }
        }

        var contextual = false;
        if (profile.BlockContextualPasswords)
        {
            var folded = Fold(password);
            foreach (var term in contextTerms)
            {
                if (term.Length < 4)
                    continue;
                if (folded.Contains(term, StringComparison.Ordinal) || term.Contains(folded, StringComparison.Ordinal))
                {
                    contextual = true;
                    issues.Add("Do not include your username, email, or the product name.");
                    suggestions.Add("Avoid names and words that are specific to this account or this platform.");
                    break;
                }
            }

            checks.Add(contextual
                ? new("context", false, "Does not contain account details")
                : new("context", true, "Does not contain account details"));
        }

        var acceptable = issues.Count == 0 && strengthOk && lengthOk && !common && !contextual;
        return new PasswordEvaluationResult(acceptable, strength, score, Distinct(issues), Distinct(suggestions), checks);
    }

    public static void EnsureAcceptable(
        string? password,
        PasswordEvaluationContext? context = null,
        PasswordPolicyProfile? profile = null)
    {
        var result = Instance.Evaluate(password, context, profile);
        if (!result.IsAcceptable)
            throw new PasswordPolicyException(result);
    }

    private static ZxcvbnAnalyzer CreateAnalyzer()
    {
        var options = new ZxcvbnOptions { MaxLength = PasswordPolicyProfile.Ordinary.MaximumLength };
        options.AddDictionary("apperture", PlatformTerms);
        return new ZxcvbnAnalyzer(options);
    }

    private static IEnumerable<string> EnumerateContextTerms(
        PasswordEvaluationContext context,
        PasswordPolicyProfile profile)
    {
        foreach (var term in PlatformTerms)
            yield return term;

        foreach (var raw in new[] { context.Username, context.DisplayName, context.Email })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            foreach (var part in SplitTerms(raw))
                yield return part;
        }

        if (context.ExtraTerms is not null)
        {
            foreach (var extra in context.ExtraTerms)
            {
                foreach (var part in SplitTerms(extra))
                    yield return part;
            }
        }

        if (profile.AdditionalContextTerms is null)
            yield break;

        foreach (var extra in profile.AdditionalContextTerms)
        {
            foreach (var part in SplitTerms(extra))
                yield return part;
        }
    }

    private static IEnumerable<string> SplitTerms(string value)
    {
        var folded = Fold(value);
        if (folded.Length >= 4)
            yield return folded;

        var at = folded.IndexOf('@');
        if (at > 0)
            yield return folded[..at];

        foreach (var part in folded.Split([' ', '.', '-', '_', '@', '+'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length >= 4)
                yield return part;
        }
    }

    private static string Fold(string value) => value.Trim().ToLowerInvariant();

    private static IReadOnlyList<string> Distinct(List<string> values)
    {
        if (values.Count <= 1)
            return values;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (seen.Add(value))
                unique.Add(value);
        }

        return unique;
    }

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.Ordinal)
    {
        "password", "password1", "password12", "password123", "password1234", "passw0rd",
        "123456", "1234567", "12345678", "123456789", "1234567890", "111111", "000000",
        "qwerty", "qwertyuiop", "qwerty123", "abc123", "abcd1234", "letmein", "welcome",
        "welcome1", "admin", "admin123", "administrator", "root", "toor", "pass",
        "iloveyou", "princess", "football", "baseball", "monkey", "dragon", "master",
        "login", "starwars", "whatever", "trustno1", "sunshine", "shadow", "superman",
        "michael", "jennifer", "hunter2", "secret", "changeme", "default", "pass1234",
        "p@ssw0rd", "p@ssword", "passw0rd!", "password!", "password1!", "password12!",
        "correcthorsebatterystaple", "correct horse battery staple",
        "apperture", "apperture1", "apperture123", "apperture!", "breakglass",
        "breakglass1", "recovery", "emergency"
    };
}
