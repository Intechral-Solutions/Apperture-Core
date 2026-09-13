namespace Apperture.Core.Domain.Identity;

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

public sealed record PasswordPolicyCheck(string Code, bool Passed, string Message);

public sealed record PasswordEvaluationResult(
    bool IsAcceptable,
    PasswordStrengthLevel Strength,
    int Score,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<PasswordPolicyCheck> Checks)
{
    public string PrimaryMessage =>
        Issues.Count > 0 ? Issues[0] : Suggestions.Count > 0 ? Suggestions[0] : "";

    /// <summary>Fine-grained estimator label. The UI meter uses <see cref="PasswordStrengthDisplay.BandLabel"/>.</summary>
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
    PasswordEvaluationResult Evaluate(
        string? password,
        PasswordEvaluationContext? context = null,
        PasswordPolicyProfile? profile = null);
}

/// <summary>
/// Operator-facing 0–4 band used by the strength meter. Color is applied in the UI; this text
/// must remain available independently of color.
/// </summary>
public static class PasswordStrengthDisplay
{
    public static bool IsNeutral(string? password) => string.IsNullOrEmpty(password);

    public static string BandLabel(int score) => score switch
    {
        0 or 1 => "Weak",
        2 => "Moderate",
        3 or 4 => "Strong",
        _ => "Weak"
    };

    public static int MeterPercent(int score, bool empty) =>
        empty ? 0 : Math.Clamp((score + 1) * 20, 0, 100);
}

public enum FieldValidationKind
{
    Pristine,
    Valid,
    Invalid
}

public sealed record FieldValidationPresentation(FieldValidationKind Kind, string? Message)
{
    public static FieldValidationPresentation Pristine { get; } = new(FieldValidationKind.Pristine, null);

    public static FieldValidationPresentation Valid { get; } = new(FieldValidationKind.Valid, null);

    public static FieldValidationPresentation Invalid(string message) => new(FieldValidationKind.Invalid, message);

    public bool ShowIndicator => Kind != FieldValidationKind.Pristine;
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
