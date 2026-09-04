namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Owns live password/confirmation values and re-evaluates them on every keystroke.
/// Callers must not re-evaluate from a stale parent copy of either field.
/// </summary>
public sealed class PasswordSetupSession
{
    public const string PasswordFieldKey = "password";
    public const string ConfirmationFieldKey = "confirmation";

    private readonly IPasswordPolicyEvaluator _evaluator;
    private PasswordPolicyProfile _profile;
    private PasswordEvaluationContext? _context;

    public PasswordSetupSession(
        IPasswordPolicyEvaluator? evaluator = null,
        PasswordPolicyProfile? profile = null,
        PasswordEvaluationContext? context = null)
    {
        _evaluator = evaluator ?? PasswordPolicyEvaluator.Instance;
        _profile = profile ?? PasswordPolicyProfile.Ordinary;
        _context = context;
        Recalculate();
    }

    public string Password { get; private set; } = "";

    public string Confirmation { get; private set; } = "";

    public bool PasswordTouched { get; private set; }

    public bool ConfirmationTouched { get; private set; }

    public bool SubmitAttempted { get; private set; }

    public PasswordSetupFormState Form { get; private set; } =
        PasswordSetupFormState.Evaluate("", "");

    public FieldValidationPresentation PasswordValidation { get; private set; } =
        FieldValidationPresentation.Pristine;

    public FieldValidationPresentation ConfirmationValidation { get; private set; } =
        FieldValidationPresentation.Pristine;

    public string? ServerPasswordError { get; private set; }

    public string? ServerConfirmationError { get; private set; }

    public bool ShowFormAttention =>
        (SubmitAttempted && !Form.IsReady)
        || !string.IsNullOrWhiteSpace(ServerPasswordError)
        || !string.IsNullOrWhiteSpace(ServerConfirmationError);

    public string? FirstInvalidFieldKey
    {
        get
        {
            if (PasswordValidation.Kind == FieldValidationKind.Invalid)
                return PasswordFieldKey;
            if (ConfirmationValidation.Kind == FieldValidationKind.Invalid)
                return ConfirmationFieldKey;
            return null;
        }
    }

    public void SetPassword(string? value)
    {
        Password = value ?? "";
        PasswordTouched = true;
        ServerPasswordError = null;
        Recalculate();
    }

    public void SetConfirmation(string? value)
    {
        Confirmation = value ?? "";
        ConfirmationTouched = true;
        ServerConfirmationError = null;
        Recalculate();
    }

    public void SetContext(PasswordEvaluationContext? context)
    {
        _context = context;
        Recalculate();
    }

    public void SetProfile(PasswordPolicyProfile? profile)
    {
        _profile = profile ?? PasswordPolicyProfile.Ordinary;
        Recalculate();
    }

    public bool ValidateForSubmit()
    {
        SubmitAttempted = true;
        Recalculate();
        return Form.IsReady;
    }

    public void ApplyServerError(string? passwordMessage = null, string? confirmationMessage = null)
    {
        SubmitAttempted = true;
        ServerPasswordError = passwordMessage;
        ServerConfirmationError = confirmationMessage;
        Recalculate();
    }

    public void Reset()
    {
        Password = "";
        Confirmation = "";
        PasswordTouched = false;
        ConfirmationTouched = false;
        SubmitAttempted = false;
        ServerPasswordError = null;
        ServerConfirmationError = null;
        Recalculate();
    }

    private void Recalculate()
    {
        Form = PasswordSetupFormState.Evaluate(
            Password,
            Confirmation,
            ConfirmationTouched || SubmitAttempted,
            _context,
            _profile,
            _evaluator);

        var passwordRevealed = PasswordTouched || SubmitAttempted;
        if (!passwordRevealed)
            PasswordValidation = FieldValidationPresentation.Pristine;
        else if (!string.IsNullOrWhiteSpace(ServerPasswordError))
            PasswordValidation = FieldValidationPresentation.Invalid(ServerPasswordError);
        else if (Form.Evaluation.IsAcceptable)
            PasswordValidation = FieldValidationPresentation.Valid;
        else
            PasswordValidation = FieldValidationPresentation.Invalid(
                string.IsNullOrWhiteSpace(Form.Evaluation.PrimaryMessage)
                    ? "Choose a password that meets the policy."
                    : Form.Evaluation.PrimaryMessage);

        var confirmationRevealed = ConfirmationTouched || SubmitAttempted;
        if (!confirmationRevealed)
            ConfirmationValidation = FieldValidationPresentation.Pristine;
        else if (!string.IsNullOrWhiteSpace(ServerConfirmationError))
            ConfirmationValidation = FieldValidationPresentation.Invalid(ServerConfirmationError);
        else if (string.IsNullOrEmpty(Confirmation))
            ConfirmationValidation = FieldValidationPresentation.Invalid("Confirm your password.");
        else if (!Form.PasswordsMatch)
            ConfirmationValidation = FieldValidationPresentation.Invalid("Passwords do not match.");
        else
            ConfirmationValidation = FieldValidationPresentation.Valid;
    }
}
