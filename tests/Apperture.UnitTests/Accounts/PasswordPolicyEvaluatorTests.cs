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
        result.Strength.Should().BeOneOf(PasswordStrengthLevel.Strong, PasswordStrengthLevel.VeryStrong);
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

        _evaluator.Evaluate(weak, emergency).IsAcceptable.Should().BeFalse();
        _evaluator.Evaluate(weak, account).IsAcceptable.Should().BeFalse();
        _evaluator.Evaluate("unique-local-passphrase", account).IsAcceptable.Should().BeTrue();
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
