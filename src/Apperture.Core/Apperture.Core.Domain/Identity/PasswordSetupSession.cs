namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Owns live password/confirmation values. Confirmation matching is immediate.
/// Guessability scoring is scheduled and coalesced so typing never waits on zxcvbn.
/// </summary>
public sealed class PasswordSetupSession
{
    public const string PasswordFieldKey = "password";
    public const string ConfirmationFieldKey = "confirmation";

    private readonly IPasswordPolicyEvaluator _evaluator;
    private readonly IPasswordEvaluationScheduler _scheduler;
    private PasswordPolicyProfile _profile;
    private PasswordEvaluationContext? _context;
    private int _scoreGeneration;
    private string _evaluatedPassword = "";

    public PasswordSetupSession(
        IPasswordPolicyEvaluator? evaluator = null,
        PasswordPolicyProfile? profile = null,
        PasswordEvaluationContext? context = null,
        IPasswordEvaluationScheduler? scheduler = null)
    {
        _evaluator = evaluator ?? PasswordPolicyEvaluator.Instance;
        _scheduler = scheduler ?? ImmediatePasswordEvaluationScheduler.Instance;
        _profile = profile ?? PasswordPolicyProfile.Ordinary;
        _context = context;
        ApplyEvaluation(_evaluator.Evaluate("", _context, _profile), "");
    }

    public event Action? EvaluationApplied;

    public string Password { get; private set; } = "";

    public string Confirmation { get; private set; } = "";

    public bool PasswordTouched { get; private set; }

    public bool ConfirmationTouched { get; private set; }

    public bool SubmitAttempted { get; private set; }

    public bool IsEvaluationPending { get; private set; }

    public PasswordSetupFormState Form { get; private set; } = null!;

    public FieldValidationPresentation PasswordValidation { get; private set; } =
        FieldValidationPresentation.Pristine;

    public FieldValidationPresentation ConfirmationValidation { get; private set; } =
        FieldValidationPresentation.Pristine;

    public string? ServerPasswordError { get; private set; }

    public string? ServerConfirmationError { get; private set; }

    public bool IsReady => !IsEvaluationPending && Form.IsReady;

    public bool ShowFormAttention =>
        (SubmitAttempted && !IsReady)
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
        RefreshImmediateState();
        ScheduleScoreIfNeeded(force: !string.Equals(_evaluatedPassword, Password, StringComparison.Ordinal));
    }

    public void SetConfirmation(string? value)
    {
        Confirmation = value ?? "";
        ConfirmationTouched = true;
        ServerConfirmationError = null;
        RefreshImmediateState();
    }

    public void SetContext(PasswordEvaluationContext? context)
    {
        if (SameContext(_context, context))
            return;
        _context = context;
        ScheduleScoreIfNeeded(force: true);
    }

    public void SetProfile(PasswordPolicyProfile? profile)
    {
        var next = profile ?? PasswordPolicyProfile.Ordinary;
        if (SameProfile(_profile, next))
            return;
        _profile = next;
        RefreshImmediateState();
        ScheduleScoreIfNeeded(force: true);
    }

    public bool ValidateForSubmit()
    {
        SubmitAttempted = true;
        _scheduler.Flush();
        if (IsEvaluationPending || !string.Equals(_evaluatedPassword, Password, StringComparison.Ordinal))
            EvaluateNow();
        RefreshImmediateState();
        return IsReady;
    }

    public void ApplyServerError(string? passwordMessage = null, string? confirmationMessage = null)
    {
        SubmitAttempted = true;
        ServerPasswordError = passwordMessage;
        ServerConfirmationError = confirmationMessage;
        RefreshImmediateState();
    }

    public void Reset()
    {
        _scheduler.Cancel();
        _scoreGeneration++;
        Password = "";
        Confirmation = "";
        PasswordTouched = false;
        ConfirmationTouched = false;
        SubmitAttempted = false;
        ServerPasswordError = null;
        ServerConfirmationError = null;
        IsEvaluationPending = false;
        ApplyEvaluation(_evaluator.Evaluate("", _context, _profile), "");
    }

    private void ScheduleScoreIfNeeded(bool force)
    {
        if (string.IsNullOrEmpty(Password))
        {
            _scheduler.Cancel();
            _scoreGeneration++;
            IsEvaluationPending = false;
            ApplyEvaluation(_evaluator.Evaluate("", _context, _profile), "");
            return;
        }

        if (!force
            && string.Equals(_evaluatedPassword, Password, StringComparison.Ordinal)
            && !IsEvaluationPending)
        {
            RefreshImmediateState();
            return;
        }

        var generation = ++_scoreGeneration;
        var password = Password;
        IsEvaluationPending = true;
        RefreshImmediateState();
        _scheduler.Schedule(() => ApplyScheduledScore(generation, password));
    }

    private void ApplyScheduledScore(int generation, string password)
    {
        if (generation != _scoreGeneration)
            return;
        if (!string.Equals(password, Password, StringComparison.Ordinal))
            return;
        EvaluateNow();
    }

    private void EvaluateNow()
    {
        ApplyEvaluation(_evaluator.Evaluate(Password, _context, _profile), Password);
    }

    private void ApplyEvaluation(PasswordEvaluationResult evaluation, string scoredPassword)
    {
        _evaluatedPassword = scoredPassword;
        IsEvaluationPending = !string.Equals(scoredPassword, Password, StringComparison.Ordinal);
        Form = new PasswordSetupFormState(
            evaluation,
            ConfirmationTouched || SubmitAttempted,
            PasswordsMatch(Password, Confirmation));
        RefreshIndicators();
        EvaluationApplied?.Invoke();
    }

    private void RefreshImmediateState()
    {
        Form = new PasswordSetupFormState(
            Form.Evaluation,
            ConfirmationTouched || SubmitAttempted,
            PasswordsMatch(Password, Confirmation));
        RefreshIndicators();
    }

    private void RefreshIndicators()
    {
        var passwordRevealed = PasswordTouched || SubmitAttempted;
        if (!passwordRevealed)
            PasswordValidation = FieldValidationPresentation.Pristine;
        else if (!string.IsNullOrWhiteSpace(ServerPasswordError))
            PasswordValidation = FieldValidationPresentation.Invalid(ServerPasswordError);
        else if (TryGetImmediatePasswordIssue(out var immediateIssue))
            PasswordValidation = FieldValidationPresentation.Invalid(immediateIssue);
        else if (IsEvaluationPending)
            PasswordValidation = FieldValidationPresentation.Pristine;
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

    private bool TryGetImmediatePasswordIssue(out string message)
    {
        if (string.IsNullOrEmpty(Password))
        {
            message = "Enter a password.";
            return true;
        }

        if (Password.Length > _profile.MaximumLength)
        {
            message = $"Password must be at most {_profile.MaximumLength} characters.";
            return true;
        }

        if (Password.Length < _profile.MinimumLength)
        {
            message = $"Use at least {_profile.MinimumLength} characters.";
            return true;
        }

        message = "";
        return false;
    }

    private static bool PasswordsMatch(string password, string confirmation) =>
        !string.IsNullOrEmpty(password)
        && string.Equals(password, confirmation, StringComparison.Ordinal);

    private static bool SameContext(PasswordEvaluationContext? left, PasswordEvaluationContext? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left == right;
    }

    private static bool SameProfile(PasswordPolicyProfile left, PasswordPolicyProfile right) =>
        left == right;
}
