namespace Apperture.Core.Domain.Identity;

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
        PasswordEvaluationContext? context = null,
        PasswordPolicyProfile? profile = null,
        IPasswordPolicyEvaluator? evaluator = null)
    {
        evaluator ??= PasswordPolicyEvaluator.Instance;
        var evaluation = evaluator.Evaluate(password, context, profile);
        var match = !string.IsNullOrEmpty(password)
            && string.Equals(password, confirmation, StringComparison.Ordinal);
        return new(evaluation, confirmationTouched, match);
    }
}
