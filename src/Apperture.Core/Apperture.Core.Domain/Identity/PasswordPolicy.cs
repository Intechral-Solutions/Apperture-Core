namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Canonical password acceptance policy. NIST/OWASP-oriented: length, blocklists, and
/// guessability — not composition rules. The implementation is replaceable via
/// <see cref="IPasswordPolicyEvaluator"/>.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;

    public const PasswordStrengthLevel MinimumAcceptableStrength = PasswordStrengthLevel.Fair;
}

public enum PasswordStrengthLevel
{
    VeryWeak = 0,
    Weak = 1,
    Fair = 2,
    Strong = 3,
    VeryStrong = 4
}

public sealed record PasswordEvaluationContext(
    string? Username = null,
    string? DisplayName = null,
    string? Email = null,
    IReadOnlyList<string>? ExtraTerms = null)
{
    public static PasswordEvaluationContext Platform { get; } = new();
}

public sealed record PasswordEvaluationResult(
    bool IsAcceptable,
    PasswordStrengthLevel Strength,
    int Score,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Suggestions)
{
    public string PrimaryMessage =>
        Issues.Count > 0 ? Issues[0] : Suggestions.Count > 0 ? Suggestions[0] : "";

    public string StrengthLabel => Strength switch
    {
        PasswordStrengthLevel.VeryStrong => "Very strong",
        PasswordStrengthLevel.Strong => "Strong",
        PasswordStrengthLevel.Fair => "Fair",
        PasswordStrengthLevel.Weak => "Weak",
        _ => "Very weak"
    };
}

public interface IPasswordPolicyEvaluator
{
    PasswordEvaluationResult Evaluate(string? password, PasswordEvaluationContext? context = null);
}

/// <summary>
/// Built-in estimator kept behind <see cref="IPasswordPolicyEvaluator"/> so a maintained
/// zxcvbn-compatible library can replace it later. Existing .NET zxcvbn ports were not
/// adopted because they are unmaintained.
/// </summary>
public sealed class PasswordPolicyEvaluator : IPasswordPolicyEvaluator
{
    public static PasswordPolicyEvaluator Instance { get; } = new();

    private static readonly string[] PlatformTerms =
    [
        "apperture", "aperture", "breakglass", "recoveryaccess", "emergencyaccess"
    ];

    public PasswordEvaluationResult Evaluate(string? password, PasswordEvaluationContext? context = null)
    {
        var issues = new List<string>();
        var suggestions = new List<string>();
        context ??= PasswordEvaluationContext.Platform;

        if (string.IsNullOrEmpty(password))
        {
            issues.Add("Enter a password.");
            suggestions.Add("Use a long passphrase of at least 12 characters.");
            return new PasswordEvaluationResult(false, PasswordStrengthLevel.VeryWeak, 0, issues, suggestions);
        }

        if (password.Length > PasswordPolicy.MaximumLength)
        {
            issues.Add($"Password must be at most {PasswordPolicy.MaximumLength} characters.");
            return new PasswordEvaluationResult(false, PasswordStrengthLevel.VeryWeak, 0, issues, suggestions);
        }

        if (password.Length < PasswordPolicy.MinimumLength)
        {
            issues.Add($"Use at least {PasswordPolicy.MinimumLength} characters.");
            suggestions.Add("A short phrase of several words is easier to remember than a short complex password.");
        }

        var folded = Fold(password);
        if (CommonPasswords.Contains(folded) || CommonPasswords.Contains(folded.Replace(" ", "", StringComparison.Ordinal)))
        {
            issues.Add("That password is too common.");
            suggestions.Add("Choose something that is not a well-known password or phrase.");
        }

        foreach (var term in EnumerateContextTerms(context))
        {
            if (term.Length < 4)
                continue;
            if (folded.Contains(term, StringComparison.Ordinal) || term.Contains(folded, StringComparison.Ordinal))
            {
                issues.Add("Do not include your username, email, or the product name.");
                suggestions.Add("Avoid names and words that are specific to this account or this platform.");
                break;
            }
        }

        var score = EstimateScore(password, folded);
        if (IsRepetitive(password) || IsSequential(folded))
        {
            score = Math.Min(score, 1);
            issues.Add("Avoid repeated or sequential characters.");
            suggestions.Add("Mix unrelated words rather than patterns like aaaa or abcd1234.");
        }

        var strength = (PasswordStrengthLevel)Math.Clamp(score, 0, 4);
        if (issues.Count == 0 && strength < PasswordPolicy.MinimumAcceptableStrength)
        {
            issues.Add("Choose a longer or less predictable passphrase.");
            suggestions.Add("Four or more unrelated words is usually enough.");
        }

        if (issues.Count == 0 && strength < PasswordStrengthLevel.Strong)
            suggestions.Add("A longer passphrase is stronger and still easy to type.");

        var acceptable = issues.Count == 0 && strength >= PasswordPolicy.MinimumAcceptableStrength;
        return new PasswordEvaluationResult(acceptable, strength, (int)strength, issues, suggestions);
    }

    public static void EnsureAcceptable(string? password, PasswordEvaluationContext? context = null)
    {
        var result = Instance.Evaluate(password, context);
        if (!result.IsAcceptable)
            throw new PasswordPolicyException(result);
    }

    private static int EstimateScore(string password, string folded)
    {
        var score = 0;
        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Length >= 16) score++;
        if (password.Length >= 20) score++;

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        if (classes >= 3 && password.Length >= 12)
            score = Math.Min(4, score + 1);

        if (password.Distinct().Count() <= 3)
            score = Math.Min(score, 1);

        if (LooksLikeSingleDictionaryWord(folded))
            score = Math.Min(score, 1);

        return score;
    }

    private static bool LooksLikeSingleDictionaryWord(string folded)
    {
        var compact = folded.Replace(" ", "", StringComparison.Ordinal);
        return compact.Length <= 16 && CommonPasswords.Contains(compact);
    }

    private static bool IsRepetitive(string password)
    {
        if (password.Length < 4)
            return false;
        if (password.Distinct().Count() == 1)
            return true;
        var half = password.Length / 2;
        return password.Length >= 8
            && string.Equals(password[..half], password.Substring(half, half), StringComparison.Ordinal);
    }

    private static bool IsSequential(string folded)
    {
        const string sequences = "abcdefghijklmnopqrstuvwxyz0123456789qwertyuiopasdfghjklzxcvbnm";
        var compact = folded.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Length < 6)
            return false;
        for (var i = 0; i <= sequences.Length - 6; i++)
        {
            var slice = sequences.Substring(i, 6);
            if (compact.Contains(slice, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateContextTerms(PasswordEvaluationContext context)
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

        if (context.ExtraTerms is null)
            yield break;
        foreach (var extra in context.ExtraTerms)
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

/// <summary>
/// Client- and server-compatible setup-form state. Confirmation is not credential state;
/// it only gates submission together with <see cref="PasswordEvaluationResult.IsAcceptable"/>.
/// </summary>
public sealed record PasswordSetupFormState(
    PasswordEvaluationResult Evaluation,
    bool ConfirmationTouched,
    bool PasswordsMatch)
{
    public bool IsReady => Evaluation.IsAcceptable && PasswordsMatch;

    public static PasswordSetupFormState Evaluate(
        string? password,
        string? confirmation,
        bool confirmationTouched = false,
        PasswordEvaluationContext? context = null)
    {
        var evaluation = PasswordPolicyEvaluator.Instance.Evaluate(password, context);
        var match = !string.IsNullOrEmpty(password)
            && string.Equals(password, confirmation, StringComparison.Ordinal);
        return new(evaluation, confirmationTouched, match);
    }
}

public sealed class PasswordPolicyException : InvalidOperationException
{
    public PasswordPolicyException(PasswordEvaluationResult evaluation)
        : base(evaluation.PrimaryMessage)
    {
        Evaluation = evaluation;
    }

    public PasswordEvaluationResult Evaluation { get; }
}
