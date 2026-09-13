using System.Diagnostics;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.UnitTests.Accounts;

public sealed class PasswordSetupSessionScoringTests
{
    [Fact]
    public void One_password_change_evaluates_once_and_parameter_resync_does_not_duplicate()
    {
        var evaluator = new CountingPasswordPolicyEvaluator();
        var session = new PasswordSetupSession(evaluator);
        var afterCtor = evaluator.Calls;

        session.SetPassword("unique-local-passphrase");
        (evaluator.Calls - afterCtor).Should().Be(1);

        session.SetProfile(PasswordPolicyProfile.Ordinary);
        session.SetContext(null);
        evaluator.Calls.Should().Be(afterCtor + 1);
    }

    [Fact]
    public void Confirmation_does_not_rescore_password()
    {
        var evaluator = new CountingPasswordPolicyEvaluator();
        var session = new PasswordSetupSession(evaluator);
        session.SetPassword("unique-local-passphrase");
        var afterPassword = evaluator.Calls;

        session.SetConfirmation("u");
        session.SetConfirmation("unique-local-passphrase");
        evaluator.Calls.Should().Be(afterPassword);
        session.Form.PasswordsMatch.Should().BeTrue();
        session.IsReady.Should().BeTrue();
    }

    [Fact]
    public void Same_context_and_profile_do_not_rescore()
    {
        var evaluator = new CountingPasswordPolicyEvaluator();
        var context = new PasswordEvaluationContext(Username: "operator", DisplayName: "Operator");
        var session = new PasswordSetupSession(evaluator, context: context);
        session.SetPassword("unique-local-passphrase");
        var afterPassword = evaluator.Calls;

        session.SetContext(new PasswordEvaluationContext(Username: "operator", DisplayName: "Operator"));
        session.SetProfile(PasswordPolicyProfile.Ordinary);
        evaluator.Calls.Should().Be(afterPassword);
    }

    [Fact]
    public void Rapid_input_coalesces_to_one_evaluation_of_the_latest_password()
    {
        var evaluator = new CountingPasswordPolicyEvaluator();
        var scheduler = new ManualPasswordEvaluationScheduler();
        var session = new PasswordSetupSession(evaluator, scheduler: scheduler);
        var afterCtor = evaluator.Calls;

        var typed = "";
        foreach (var ch in "Apper")
        {
            typed += ch;
            session.SetPassword(typed);
            session.Form.PasswordsMatch.Should().BeFalse();
            session.IsEvaluationPending.Should().BeTrue();
            session.IsReady.Should().BeFalse();
        }

        scheduler.ScheduleCount.Should().Be(5);
        evaluator.Calls.Should().Be(afterCtor);
        scheduler.HasPending.Should().BeTrue();

        scheduler.Flush();
        evaluator.Calls.Should().Be(afterCtor + 1);
        evaluator.LastPassword.Should().Be("Apper");
        session.Password.Should().Be("Apper");
        session.IsEvaluationPending.Should().BeFalse();
    }

    [Fact]
    public void Stale_scheduled_result_cannot_overwrite_a_newer_password()
    {
        var evaluator = new CountingPasswordPolicyEvaluator();
        var scheduler = new ManualPasswordEvaluationScheduler();
        var session = new PasswordSetupSession(evaluator, scheduler: scheduler);

        session.SetPassword("first-value");
        session.SetPassword("unique-local-passphrase");
        session.SetConfirmation("unique-local-passphrase");
        session.IsReady.Should().BeFalse();

        scheduler.Flush();
        evaluator.LastPassword.Should().Be("unique-local-passphrase");
        session.IsReady.Should().BeTrue();
        session.Form.Evaluation.IsAcceptable.Should().BeTrue();
    }

    [Fact]
    public void ValidateForSubmit_flushes_pending_evaluation()
    {
        var evaluator = new CountingPasswordPolicyEvaluator();
        var scheduler = new ManualPasswordEvaluationScheduler();
        var session = new PasswordSetupSession(evaluator, scheduler: scheduler);
        var afterCtor = evaluator.Calls;

        session.SetPassword("unique-local-passphrase");
        session.SetConfirmation("unique-local-passphrase");
        evaluator.Calls.Should().Be(afterCtor);
        session.IsReady.Should().BeFalse();

        session.ValidateForSubmit().Should().BeTrue();
        evaluator.Calls.Should().Be(afterCtor + 1);
        session.IsEvaluationPending.Should().BeFalse();
        session.IsReady.Should().BeTrue();
    }

    [Fact]
    public void Confirmation_match_updates_immediately_while_score_is_pending()
    {
        var scheduler = new ManualPasswordEvaluationScheduler();
        var session = new PasswordSetupSession(scheduler: scheduler);
        session.SetPassword("unique-local-passphrase");
        session.SetConfirmation("unique-local-passphrase");
        session.Form.PasswordsMatch.Should().BeTrue();
        session.ConfirmationValidation.Kind.Should().Be(FieldValidationKind.Valid);
        session.IsEvaluationPending.Should().BeTrue();
        session.IsReady.Should().BeFalse();
    }

    [Fact]
    public void Reset_cancels_pending_evaluation_and_clears_state()
    {
        var evaluator = new CountingPasswordPolicyEvaluator();
        var scheduler = new ManualPasswordEvaluationScheduler();
        var session = new PasswordSetupSession(evaluator, scheduler: scheduler);
        session.SetPassword("unique-local-passphrase");
        session.SetConfirmation("unique-local-passphrase");
        var afterTyping = evaluator.Calls;

        session.Reset();
        scheduler.HasPending.Should().BeFalse();
        session.Password.Should().BeEmpty();
        session.IsEvaluationPending.Should().BeFalse();
        session.IsReady.Should().BeFalse();
        session.PasswordValidation.Kind.Should().Be(FieldValidationKind.Pristine);
        evaluator.Calls.Should().Be(afterTyping + 1);
        evaluator.LastPassword.Should().BeEmpty();
    }

    [Fact]
    public void Context_change_rescores_current_password_once()
    {
        var evaluator = new CountingPasswordPolicyEvaluator();
        var session = new PasswordSetupSession(evaluator);
        session.SetPassword("a quiet harbor lamp beside the river");
        var afterPassword = evaluator.Calls;

        session.SetContext(new PasswordEvaluationContext(Username: "harbor"));
        evaluator.Calls.Should().Be(afterPassword + 1);
        session.Form.Evaluation.IsAcceptable.Should().BeFalse();
    }
}

public sealed class PasswordEvaluationDesktopTimingDiagnostics
{
    private readonly ITestOutputHelper _output;

    public PasswordEvaluationDesktopTimingDiagnostics(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Records_desktop_zxcvbn_timings_for_representative_passwords()
    {
        var evaluator = PasswordPolicyEvaluator.Instance;
        evaluator.Evaluate("warmup-passphrase-value");

        var samples = new (string Label, string Password)[]
        {
            ("5 random", "xq7m2"),
            ("10 random", "xq7m2p9kva"),
            ("15 random", "xq7m2p9kva4n8qw"),
            ("20 random", "xq7m2p9kva4n8qw3zlt"),
            ("30 random", "xq7m2p9kva4n8qw3zlt5b8cm1d0ef"),
            ("50 random", "xq7m2p9kva4n8qw3zlt5b8cm1d0ef9g2hj4k6lm8n0pq"),
            ("100 random", new string('x', 50) + "q7m2p9kva4n8qw3zlt5b8cm1d0ef9g2hj4k6lm8n0pqrstu"),
            ("15 repeat", new string('a', 15)),
            ("30 repeat", new string('a', 30)),
            ("50 repeat", new string('a', 50)),
            ("100 repeat", new string('a', 100)),
            ("10 common", "password12"),
            ("30 dictionary", "correct horse battery staple"),
            ("15 context", "apperture-rocks"),
            ("30 passphrase", "a quiet harbor lamp beside"),
        };

        var context = new PasswordEvaluationContext(Username: "localuser", DisplayName: "Operator", Email: "operator@example.com");
        foreach (var (label, password) in samples)
        {
            var times = new List<long>(5);
            for (var i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                evaluator.Evaluate(password, context);
                sw.Stop();
                times.Add(sw.ElapsedMilliseconds);
            }

            times.Sort();
            _output.WriteLine(
                "{0,-16} len={1,3} median={2}ms worst={3}ms",
                label,
                password.Length,
                times[2],
                times[^1]);
        }
    }
}

internal sealed class CountingPasswordPolicyEvaluator : IPasswordPolicyEvaluator
{
    private readonly IPasswordPolicyEvaluator _inner;

    public CountingPasswordPolicyEvaluator(IPasswordPolicyEvaluator? inner = null) =>
        _inner = inner ?? PasswordPolicyEvaluator.Instance;

    public int Calls { get; private set; }

    public string? LastPassword { get; private set; }

    public PasswordEvaluationResult Evaluate(
        string? password,
        PasswordEvaluationContext? context = null,
        PasswordPolicyProfile? profile = null)
    {
        Calls++;
        LastPassword = password;
        return _inner.Evaluate(password, context, profile);
    }
}
