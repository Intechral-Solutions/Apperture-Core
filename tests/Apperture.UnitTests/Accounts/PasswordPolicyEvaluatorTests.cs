using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Accounts;

public sealed class PasswordPolicyEvaluatorTests
{
    private readonly IPasswordPolicyEvaluator _evaluator = PasswordPolicyEvaluator.Instance;

    [Theory]
    [InlineData("password")]
    [InlineData("Password1!")]
    [InlineData("123456789012")]
    [InlineData("qwertyuiop")]
    [InlineData("correct horse battery staple")]
    public void Rejects_common_or_short_passwords(string password)
    {
        var result = _evaluator.Evaluate(password);
        result.IsAcceptable.Should().BeFalse();
        result.Issues.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("apperture-rocks-now")]
    [InlineData("breakglass-secret")]
    [InlineData("operator-localuser-1")]
    public void Rejects_contextual_platform_or_username_derivatives(string password)
    {
        var result = _evaluator.Evaluate(
            password,
            new PasswordEvaluationContext(Username: "localuser", DisplayName: "Operator", Email: "operator@example.com"));
        result.IsAcceptable.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Contains("username", StringComparison.OrdinalIgnoreCase)
            || i.Contains("product", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Accepts_a_long_unique_passphrase_without_composition_rules()
    {
        var result = _evaluator.Evaluate("a quiet harbor lamp beside the river");
        result.IsAcceptable.Should().BeTrue();
        result.Score.Should().BeGreaterThanOrEqualTo(PasswordPolicy.MinimumAcceptableScore);
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Does_not_require_uppercase_digit_or_symbol()
    {
        var result = _evaluator.Evaluate("unique local passphrase only words");
        result.IsAcceptable.Should().BeTrue();
    }

    [Fact]
    public void Rejects_passwords_longer_than_the_safe_maximum()
    {
        var result = _evaluator.Evaluate(new string('a', PasswordPolicy.MaximumLength + 1));
        result.IsAcceptable.Should().BeFalse();
    }

    [Fact]
    public void Same_evaluator_rejects_weak_values_for_emergency_and_account_context()
    {
        var weak = "password";
        var emergency = new PasswordEvaluationContext(
            Username: EmergencyAccessIdentity.LocalUsername,
            DisplayName: EmergencyAccessIdentity.DisplayName);
        var account = new PasswordEvaluationContext(Username: "operator");

        _evaluator.Evaluate(weak, emergency, PasswordPolicyProfile.RecoveryAccess).IsAcceptable.Should().BeFalse();
        _evaluator.Evaluate(weak, account).IsAcceptable.Should().BeFalse();
        _evaluator.Evaluate("unique-local-passphrase", account).IsAcceptable.Should().BeTrue();
    }

    [Fact]
    public void Ordinary_profile_requires_fifteen_characters()
    {
        PasswordPolicyProfile.Ordinary.MinimumLength.Should().Be(15);
        var result = _evaluator.Evaluate("twelvechars!!");
        result.IsAcceptable.Should().BeFalse();
        result.Checks.Should().Contain(c => c.Code == "length" && !c.Passed);
    }

    [Fact]
    public void Recovery_profile_rejects_an_ordinary_length_password()
    {
        const string fifteen = "maple lanterns!";
        fifteen.Length.Should().Be(15);
        var recovery = _evaluator.Evaluate(fifteen, profile: PasswordPolicyProfile.RecoveryAccess);
        recovery.IsAcceptable.Should().BeFalse();
        recovery.Checks.Should().Contain(c => c.Code == "length" && !c.Passed);
        PasswordPolicyProfile.RecoveryAccess.MinimumLength.Should().BeGreaterThan(PasswordPolicyProfile.Ordinary.MinimumLength);
    }

    [Fact]
    public void Strength_can_be_strong_while_policy_still_rejects()
    {
        var result = _evaluator.Evaluate("apperture-rocks-now!!");
        result.IsAcceptable.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Contains("product", StringComparison.OrdinalIgnoreCase)
            || i.Contains("username", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Empty_password_is_not_acceptable_and_scores_zero()
    {
        var result = _evaluator.Evaluate("");
        result.IsAcceptable.Should().BeFalse();
        result.Score.Should().Be(0);
    }

    [Fact]
    public void Score_changes_as_the_typed_value_grows()
    {
        var previous = -1;
        var sawIncrease = false;
        var typed = "";
        foreach (var ch in "a quiet harbor lamp beside the river")
        {
            typed += ch;
            var score = _evaluator.Evaluate(typed).Score;
            if (score > previous && previous >= 0)
                sawIncrease = true;
            previous = score;
        }

        sawIncrease.Should().BeTrue();
        _evaluator.Evaluate(typed).IsAcceptable.Should().BeTrue();
    }
}

public sealed class PasswordStrengthDisplayTests
{
    [Theory]
    [InlineData(0, "Weak")]
    [InlineData(1, "Weak")]
    [InlineData(2, "Moderate")]
    [InlineData(3, "Strong")]
    [InlineData(4, "Strong")]
    public void Band_labels_follow_the_zero_to_four_mapping(int score, string expected)
    {
        PasswordStrengthDisplay.BandLabel(score).Should().Be(expected);
    }

    [Fact]
    public void Empty_password_is_neutral_and_has_no_meter_fill()
    {
        PasswordStrengthDisplay.IsNeutral("").Should().BeTrue();
        PasswordStrengthDisplay.IsNeutral(null).Should().BeTrue();
        PasswordStrengthDisplay.IsNeutral("a").Should().BeFalse();
        PasswordStrengthDisplay.MeterPercent(0, empty: true).Should().Be(0);
        PasswordStrengthDisplay.MeterPercent(0, empty: false).Should().Be(20);
        PasswordStrengthDisplay.MeterPercent(4, empty: false).Should().Be(100);
    }
}

public sealed class PasswordSetupFormStateTests
{
    [Fact]
    public void Mismatch_is_not_ready()
    {
        var state = PasswordSetupFormState.Evaluate("unique-local-passphrase", "different-passphrase", confirmationTouched: true);
        state.Evaluation.IsAcceptable.Should().BeTrue();
        state.PasswordsMatch.Should().BeFalse();
        state.IsReady.Should().BeFalse();
    }

    [Fact]
    public void Matching_but_weak_password_is_not_ready()
    {
        var state = PasswordSetupFormState.Evaluate("password", "password", confirmationTouched: true);
        state.Evaluation.IsAcceptable.Should().BeFalse();
        state.PasswordsMatch.Should().BeTrue();
        state.IsReady.Should().BeFalse();
    }

    [Fact]
    public void Strong_and_matching_password_is_ready()
    {
        var state = PasswordSetupFormState.Evaluate("unique-local-passphrase", "unique-local-passphrase", confirmationTouched: true);
        state.IsReady.Should().BeTrue();
        state.Evaluation.IsAcceptable.Should().BeTrue();
        state.PasswordsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluation_updates_as_the_typed_value_changes()
    {
        var first = PasswordSetupFormState.Evaluate("short", "short", confirmationTouched: true);
        var next = PasswordSetupFormState.Evaluate("unique-local-passphrase", "unique-local-passphrase", confirmationTouched: true);
        first.IsReady.Should().BeFalse();
        next.IsReady.Should().BeTrue();
        next.Evaluation.Score.Should().BeGreaterThan(first.Evaluation.Score);
    }

    [Fact]
    public void Empty_reset_state_is_not_ready()
    {
        var state = PasswordSetupFormState.Evaluate("", "", confirmationTouched: false);
        state.IsReady.Should().BeFalse();
        state.PasswordsMatch.Should().BeFalse();
        state.ConfirmationTouched.Should().BeFalse();
    }
}

public sealed class PasswordSetupSessionTests
{
    [Fact]
    public void Starts_empty_without_application_provided_password()
    {
        var session = new PasswordSetupSession();
        session.Password.Should().BeEmpty();
        session.Confirmation.Should().BeEmpty();
        session.PasswordValidation.Kind.Should().Be(FieldValidationKind.Pristine);
        session.ConfirmationValidation.Kind.Should().Be(FieldValidationKind.Pristine);
        session.ShowFormAttention.Should().BeFalse();
        PasswordStrengthDisplay.IsNeutral(session.Password).Should().BeTrue();
    }

    [Fact]
    public void Strength_and_match_recalculate_from_the_current_keystroke_not_a_stale_copy()
    {
        var session = new PasswordSetupSession();
        var scores = new List<int>();

        foreach (var ch in "unique-local-passphrase")
        {
            session.SetPassword(session.Password + ch);
            scores.Add(session.Form.Evaluation.Score);
            session.Form.PasswordsMatch.Should().BeFalse();
        }

        scores.Should().Contain(s => s < scores[scores.Count - 1]);
        session.SetConfirmation("unique-local-passphrase");
        session.Form.PasswordsMatch.Should().BeTrue();
        session.Form.IsReady.Should().BeTrue();
        session.ConfirmationValidation.Kind.Should().Be(FieldValidationKind.Valid);

        session.SetPassword(session.Password + "x");
        session.Form.PasswordsMatch.Should().BeFalse();
        session.ConfirmationValidation.Kind.Should().Be(FieldValidationKind.Invalid);

        session.SetConfirmation(session.Password);
        session.Form.PasswordsMatch.Should().BeTrue();
    }

    [Fact]
    public void Equal_password_and_confirmation_report_a_match_immediately()
    {
        var session = new PasswordSetupSession();
        session.SetPassword("unique-local-passphrase");
        session.SetConfirmation("unique-local-passphrase");
        session.Form.PasswordsMatch.Should().BeTrue();
        session.Form.IsReady.Should().BeTrue();
    }

    [Fact]
    public void Submit_reveals_remaining_invalid_fields_without_being_ready()
    {
        var session = new PasswordSetupSession();
        session.ValidateForSubmit().Should().BeFalse();
        session.ShowFormAttention.Should().BeTrue();
        session.PasswordValidation.Kind.Should().Be(FieldValidationKind.Invalid);
        session.ConfirmationValidation.Kind.Should().Be(FieldValidationKind.Invalid);
        session.FirstInvalidFieldKey.Should().Be(PasswordSetupSession.PasswordFieldKey);
    }

    [Fact]
    public void Touched_valid_and_invalid_indicators()
    {
        var session = new PasswordSetupSession();
        session.SetPassword("password");
        session.PasswordValidation.Kind.Should().Be(FieldValidationKind.Invalid);
        session.PasswordValidation.Message.Should().NotBeNullOrWhiteSpace();
        session.ConfirmationValidation.Kind.Should().Be(FieldValidationKind.Pristine);

        session.SetPassword("unique-local-passphrase");
        session.PasswordValidation.Kind.Should().Be(FieldValidationKind.Valid);

        session.SetConfirmation("nope");
        session.ConfirmationValidation.Kind.Should().Be(FieldValidationKind.Invalid);
        session.SetConfirmation("unique-local-passphrase");
        session.ConfirmationValidation.Kind.Should().Be(FieldValidationKind.Valid);
    }

    [Fact]
    public void Matching_weak_password_remains_invalid()
    {
        var session = new PasswordSetupSession();
        session.SetPassword("password");
        session.SetConfirmation("password");
        session.Form.PasswordsMatch.Should().BeTrue();
        session.Form.IsReady.Should().BeFalse();
        session.PasswordValidation.Kind.Should().Be(FieldValidationKind.Invalid);
    }

    [Fact]
    public void Strong_but_policy_ineligible_password_remains_invalid()
    {
        var session = new PasswordSetupSession(context: new PasswordEvaluationContext(Username: "harbor"));
        session.SetPassword("a quiet harbor lamp beside the river");
        session.SetConfirmation("a quiet harbor lamp beside the river");
        session.Form.PasswordsMatch.Should().BeTrue();
        session.Form.Evaluation.IsAcceptable.Should().BeFalse();
        session.Form.IsReady.Should().BeFalse();
    }

    [Fact]
    public void Server_field_errors_appear_on_the_same_validation_presentation()
    {
        var session = new PasswordSetupSession();
        session.SetPassword("unique-local-passphrase");
        session.SetConfirmation("unique-local-passphrase");
        session.ApplyServerError("Current password is incorrect.");
        session.PasswordValidation.Kind.Should().Be(FieldValidationKind.Invalid);
        session.PasswordValidation.Message.Should().Be("Current password is incorrect.");
        session.ShowFormAttention.Should().BeTrue();
    }

    [Fact]
    public void Reset_clears_values_strength_and_validation()
    {
        var session = new PasswordSetupSession();
        session.SetPassword("unique-local-passphrase");
        session.SetConfirmation("unique-local-passphrase");
        session.ValidateForSubmit();
        session.Reset();
        session.Password.Should().BeEmpty();
        session.Confirmation.Should().BeEmpty();
        session.PasswordValidation.Kind.Should().Be(FieldValidationKind.Pristine);
        session.Form.IsReady.Should().BeFalse();
        PasswordStrengthDisplay.IsNeutral(session.Password).Should().BeTrue();
    }
}
