using Apperture.Cli.Host;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Cli;

/// <summary>
/// The emergency commands over strict doubles of the application services: a call nobody set up fails the case, so a command that reaches
/// a service it should not (a reset instead of a replacement, an enrolment after a refusal) cannot pass.
/// </summary>
public sealed class EmergencyCommandRunnerTests
{
    private const string NewCode = "123456";

    // ------------------------------------------------------------------------------------------------ the replacement

    [Fact]
    public async Task The_replacement_stages_verifies_and_switches_and_shows_the_codes_only_after_the_switch_returned()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyAccepts(NewCode);
        w.SwitchReturns(new NativeMfaReplacementResult(false, CliWorld.NewCodes));
        var console = w.Console(redirected: false, NewCode);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.Success);
        w.Mfa.Verify(m => m.BeginReplacementAsync(CliWorld.Id, "breakglass", It.IsAny<CancellationToken>()), Times.Once);
        w.Mfa.Verify(m => m.VerifyReplacementAsync(CliWorld.Id, "cand-1", 1, NewCode, It.IsAny<CancellationToken>()), Times.Once);
        w.Mfa.Verify(m => m.CompleteReplacementAsync(CliWorld.Id, "cand-1", 2, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()), Times.Once);
        w.Mfa.Verify(m => m.ResetAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Never);

        var switched = console.Events.IndexOf("service:switch-returned");
        switched.Should().BeGreaterThan(-1);
        foreach (var code in CliWorld.NewCodes)
            console.Events.IndexOf($"out:{code}").Should().BeGreaterThan(switched, "a recovery code is shown only once the switch has returned");
        console.Events.Where(e => e.StartsWith("out:")).Take(5).Should().NotContain(e => CliWorld.NewCodes.Any(c => e.Contains(c)));
        console.OutText.ToString().Should().Contain("STAGEDSECRETKEY").And.Contain("will not be shown again");
        console.ErrorText.ToString().Should().NotContain(NewCode);
    }

    [Fact]
    public async Task The_staged_secret_is_shown_before_the_code_is_asked_for_and_the_current_authenticator_is_said_to_keep_working()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyAccepts(NewCode);
        w.SwitchReturns(new NativeMfaReplacementResult(false, CliWorld.NewCodes));
        var console = w.Console(redirected: false, NewCode);

        await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        console.Events.IndexOf("out:Manual key: STAGEDSECRETKEY").Should().BeLessThan(console.Events.IndexOf("read"));
        console.OutText.ToString().Should().Contain("keep working until the replacement is verified");
    }

    [Fact]
    public async Task The_replacement_of_an_account_with_no_authenticator_is_refused_and_never_becomes_a_reset_or_an_enrolment()
    {
        var w = new CliWorld();
        w.Mfa.Setup(m => m.BeginReplacementAsync(CliWorld.Id, "breakglass", It.IsAny<CancellationToken>()))
            .Returns(() => CliWorld.Faulted<NativeMfaReplacementChallenge>(
                new GovernanceConflictException(GovernanceConflictCodes.AuthenticatorNotEnrolled, "There is no authenticator to replace.")));
        var console = w.Console(redirected: false);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.Refused);
        console.ErrorText.ToString().Should().Contain("[authenticator_not_enrolled]").And.Contain("enroll-mfa");
        w.Mfa.Verify(m => m.ResetAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Mfa.Verify(m => m.BeginEnrollmentAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        console.Reads.Should().Be(0);
    }

    [Fact]
    public async Task A_wrong_code_at_a_prompt_is_asked_for_again_and_a_correct_one_completes()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyRejects("000000");
        w.VerifyAccepts(NewCode);
        w.SwitchReturns(new NativeMfaReplacementResult(false, CliWorld.NewCodes));
        var console = w.Console(redirected: false, "000000", NewCode);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.Success);
        console.Reads.Should().Be(2);
        console.ErrorText.ToString().Should().Contain("That code is not valid.");
    }

    [Fact]
    public async Task Three_wrong_codes_discard_the_staged_replacement_and_switch_nothing()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyRejects("000000");
        w.AbandonReturns();
        var console = w.Console(redirected: false, "000000", "000000", "000000", "000000");

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.InvalidUsage);
        console.Reads.Should().Be(EmergencyCommandRunner.MaxCodeAttempts);
        console.Events.Should().Contain("service:abandoned");
        w.Mfa.Verify(m => m.CompleteReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Never);
        console.ErrorText.ToString().Should().Contain("current authenticator is unchanged");
    }

    [Fact]
    public async Task When_input_is_piped_a_wrong_code_is_not_asked_for_again()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyRejects("000000");
        w.AbandonReturns();
        var console = w.Console(redirected: true, "000000", NewCode);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.InvalidUsage);
        console.Reads.Should().Be(1);
        w.Mfa.Verify(m => m.CompleteReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task When_input_ends_before_a_code_the_replacement_is_discarded_and_nothing_is_switched()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.AbandonReturns();
        var console = w.Console(redirected: true);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.InvalidUsage);
        console.ErrorText.ToString().Should().Contain("No input.").And.Contain("No code was entered");
        console.Events.Should().Contain("service:abandoned");
        w.Mfa.Verify(m => m.VerifyReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancelling_while_waiting_for_the_code_discards_the_staged_replacement_and_changes_nothing()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.AbandonReturns();
        var console = w.Console(redirected: false, new OperationCanceledException());

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.Refused);
        console.Events.Should().Contain("service:abandoned");
        console.ErrorText.ToString().Should().Contain("Cancelled").And.Contain("current authenticator is unchanged");
        w.Mfa.Verify(m => m.CompleteReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_discard_that_fails_still_ends_the_command_with_the_active_authenticator_said_to_be_unaffected()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.Mfa.Setup(m => m.AbandonReplacementAsync(CliWorld.Id, It.IsAny<CancellationToken>()))
            .Returns(() => CliWorld.Faulted<bool>(new TimeoutException()));
        var console = w.Console(redirected: true);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.InvalidUsage);
        console.ErrorText.ToString().Should().Contain("never affects the current authenticator");
    }

    [Fact]
    public async Task A_staged_replacement_that_expired_before_the_code_is_a_retryable_conflict_and_is_not_switched()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyThrows(new GovernanceConflictException(GovernanceConflictCodes.AuthenticatorReplacementExpired, "The replacement expired."));
        var console = w.Console(redirected: false, NewCode);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.Retryable);
        console.ErrorText.ToString().Should().Contain("[authenticator_replacement_expired]").And.Contain("still active");
        w.Mfa.Verify(m => m.CompleteReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Mfa.Verify(m => m.AbandonReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<CancellationToken>()), Times.Never,
            "a replacement that is no longer ours is not discarded: a newer one may be another operator's");
    }

    [Theory]
    [InlineData(GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid)]
    [InlineData(GovernanceConflictCodes.AuthenticatorChanged)]
    public async Task A_consumed_superseded_or_changed_replacement_at_the_code_is_a_retryable_conflict(string code)
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyThrows(new GovernanceConflictException(code, "m"));
        var console = w.Console(redirected: false, NewCode);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.Retryable);
        w.Mfa.Verify(m => m.CompleteReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_repeated_switch_of_an_already_applied_candidate_succeeds_and_shows_no_codes()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyAccepts(NewCode);
        w.SwitchReturns(NativeMfaReplacementResult.AppliedEarlier);
        var console = w.Console(redirected: false, NewCode);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.Success);
        console.OutText.ToString().Should().Contain("already been applied").And.Contain("cannot be shown again").And.Contain("regenerate-codes");
        console.OutText.ToString().Should().NotContain("will not be shown again", "no codes are listed for a switch an earlier request made");
    }

    // ---- The switch's own failures: nothing is shown as active unless the switch returned ----

    public static IEnumerable<object[]> SwitchFailures() =>
    [
        [new GovernanceConflictException(GovernanceConflictCodes.EntraIndependentFallbackRequired, "Refused."), EmergencyExitCodes.Refused],
        [new GovernanceConflictException(GovernanceConflictCodes.LastViableAdministrator, "Refused."), EmergencyExitCodes.Refused],
        [new GovernanceConflictException(GovernanceConflictCodes.RecoveryAccessPostureReduced, "Reduced."), EmergencyExitCodes.Retryable],
        [new GovernanceConflictException(GovernanceConflictCodes.AdministratorAccessChanged, "Changed."), EmergencyExitCodes.Retryable],
        [new GovernanceConflictException(GovernanceConflictCodes.AuthenticatorChanged, "Changed."), EmergencyExitCodes.Retryable],
        [new GovernanceConflictException(GovernanceConflictCodes.AdministratorViabilityUnavailable, "No evidence."), EmergencyExitCodes.EvidenceUnavailable],
        [new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.TimedOut), EmergencyExitCodes.Retryable],
        [new AuditOperationNotCompletedException("op-1"), EmergencyExitCodes.Retryable],
        [new AuditOutcomeUnknownException("op-1"), EmergencyExitCodes.OutcomeUnknown],
        [new AuditCommittedResultUnavailableException("op-1", new Exception("x")), EmergencyExitCodes.OutcomeUnknown],
        [new AuditInvariantException("defect"), EmergencyExitCodes.InternalFailure],
        [new InvalidOperationException("unexpected"), EmergencyExitCodes.InternalFailure],
    ];

    [Theory]
    [MemberData(nameof(SwitchFailures))]
    public async Task A_switch_that_did_not_return_shows_no_recovery_code_and_exits_with_its_status(Exception thrown, int expected)
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyAccepts(NewCode);
        w.SwitchThrows(thrown);
        var console = w.Console(redirected: false, NewCode);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(expected);
        foreach (var code in CliWorld.NewCodes)
            console.AllOutput.Should().NotContain(code);
        console.OutText.ToString().Should().NotContain("will not be shown again").And.NotContain("was replaced");
        w.Mfa.Verify(m => m.ResetAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Mfa.Verify(m => m.BeginEnrollmentAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Mfa.Verify(m => m.AbandonReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<CancellationToken>()), Times.Never,
            "after a switch whose outcome may be applied, the staged replacement is never discarded");
    }

    [Fact]
    public async Task An_unknown_outcome_tells_the_operator_to_check_and_not_to_repeat()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyAccepts(NewCode);
        w.SwitchThrows(new AuditOutcomeUnknownException("op-77"));
        var console = w.Console(redirected: false, NewCode);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.OutcomeUnknown);
        console.ErrorText.ToString().Should().Contain("may or may not have been applied").And.Contain("op-77").And.Contain("emergency status").And.Contain("Do not repeat");
        console.ErrorText.ToString().Should().NotContain("Nothing was changed");
    }

    [Fact]
    public async Task A_switch_that_returns_no_usable_codes_does_not_show_a_blank_set_as_active()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyAccepts(NewCode);
        w.SwitchReturns(new NativeMfaReplacementResult(false, []));
        var console = w.Console(redirected: false, NewCode);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"));

        exit.Should().Be(EmergencyExitCodes.OutcomeUnknown);
        console.OutText.ToString().Should().NotContain("will not be shown again");
        console.ErrorText.ToString().Should().Contain("no usable recovery codes").And.Contain("regenerate-codes");
    }

    // ------------------------------------------------------------------------------------------------ the bare reset

    [Fact]
    public async Task The_bare_reset_is_run_only_when_asked_for_and_calls_only_the_reset()
    {
        var w = new CliWorld();
        w.Mfa.Setup(m => m.ResetAsync(CliWorld.Id, AuditActor.EmergencyCli, It.IsAny<CancellationToken>())).Returns(() => ValueTask.CompletedTask);
        var console = w.Console(redirected: false);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa", "--bare-reset"));

        exit.Should().Be(EmergencyExitCodes.Success);
        w.Mfa.Verify(m => m.ResetAsync(CliWorld.Id, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()), Times.Once);
        w.Mfa.Verify(m => m.BeginReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Mfa.Verify(m => m.BeginEnrollmentAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        console.Reads.Should().Be(0, "a bare reset asks for nothing");
        console.OutText.ToString().Should().Contain("Bare reset complete").And.Contain("enroll-mfa");
    }

    [Fact]
    public async Task The_replacement_never_calls_the_reset()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyAccepts(NewCode);
        w.SwitchReturns(new NativeMfaReplacementResult(false, CliWorld.NewCodes));

        await w.RunAsync(w.Console(redirected: false, NewCode), CliWorld.OnAccount("reset-mfa"));

        w.Mfa.Verify(m => m.ResetAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<AuditActor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_bare_reset_the_viability_rules_refuse_reports_the_refusal_and_does_not_fall_back_to_a_replacement()
    {
        var w = new CliWorld();
        w.Mfa.Setup(m => m.ResetAsync(CliWorld.Id, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask(Task.FromException(new GovernanceConflictException(
                GovernanceConflictCodes.EntraIndependentFallbackRequired,
                "Cannot reset this authenticator: Recovery Access would stop being healthy. Replace the authenticator instead of resetting it."))));
        var console = w.Console(redirected: false);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("reset-mfa", "--bare-reset"));

        exit.Should().Be(EmergencyExitCodes.Refused);
        console.ErrorText.ToString().Should().Contain("[entra_independent_fallback_required]").And.Contain("Cannot reset this authenticator");
        w.Mfa.Verify(m => m.BeginReplacementAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Mfa.Verify(m => m.BeginEnrollmentAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        console.OutText.ToString().Should().NotContain("Bare reset complete");
    }

    [Theory]
    [InlineData(GovernanceConflictCodes.AdministratorViabilityUnavailable, EmergencyExitCodes.EvidenceUnavailable)]
    [InlineData(GovernanceConflictCodes.LastViableAdministrator, EmergencyExitCodes.Refused)]
    public async Task A_bare_reset_without_the_evidence_or_the_viability_is_refused_with_the_matching_status(string code, int expected)
    {
        var w = new CliWorld();
        w.Mfa.Setup(m => m.ResetAsync(CliWorld.Id, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask(Task.FromException(new GovernanceConflictException(code, "refused"))));

        var exit = await w.RunAsync(w.Console(redirected: false), CliWorld.OnAccount("reset-mfa", "--bare-reset"));

        exit.Should().Be(expected);
    }

    [Fact]
    public async Task A_bare_reset_that_is_busy_or_unknown_maps_to_its_own_status()
    {
        var busy = new CliWorld();
        busy.Mfa.Setup(m => m.ResetAsync(CliWorld.Id, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask(Task.FromException(new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.TimedOut))));
        (await busy.RunAsync(busy.Console(false), CliWorld.OnAccount("reset-mfa", "--bare-reset"))).Should().Be(EmergencyExitCodes.Retryable);

        var unknown = new CliWorld();
        unknown.Mfa.Setup(m => m.ResetAsync(CliWorld.Id, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask(Task.FromException(new AuditOutcomeUnknownException("op-1"))));
        var console = unknown.Console(false);
        (await unknown.RunAsync(console, CliWorld.OnAccount("reset-mfa", "--bare-reset"))).Should().Be(EmergencyExitCodes.OutcomeUnknown);
        console.OutText.ToString().Should().NotContain("Bare reset complete");
    }

    // ------------------------------------------------------------------------------------------------ the first enrolment

    [Fact]
    public async Task Enrolment_of_an_enrolled_account_is_refused_before_anything_is_staged()
    {
        var w = new CliWorld();
        w.Enrolled(true);
        var console = w.Console(redirected: false);

        var exit = await w.RunAsync(console, CliWorld.OnAccount("enroll-mfa"));

        exit.Should().Be(EmergencyExitCodes.Refused);
        console.ErrorText.ToString().Should().Contain("already enrolled").And.Contain("reset-mfa");
        w.Mfa.Verify(m => m.BeginEnrollmentAsync(It.IsAny<Apperture.Core.Domain.Identity.UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enrolment_shows_the_codes_only_after_it_returned_and_a_wrong_code_shows_none()
    {
        var wrong = new CliWorld();
        wrong.Enrolled(false);
        wrong.Mfa.Setup(m => m.BeginEnrollmentAsync(CliWorld.Id, "breakglass", It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new NativeMfaEnrollmentChallenge(CliWorld.Id, "otpauth://x", "KEY", [])));
        wrong.Mfa.Setup(m => m.CompleteEnrollmentAsync(CliWorld.Id, "000000", AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() => CliWorld.Faulted<IReadOnlyList<string>>(new NativeMfaCodeNotValidException()));
        var console = wrong.Console(redirected: false, "000000");

        (await wrong.RunAsync(console, CliWorld.OnAccount("enroll-mfa"))).Should().Be(EmergencyExitCodes.InvalidUsage);
        console.AllOutput.Should().NotContain("recovery codes now");

        var right = new CliWorld();
        right.Enrolled(false);
        right.Mfa.Setup(m => m.BeginEnrollmentAsync(CliWorld.Id, "breakglass", It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new NativeMfaEnrollmentChallenge(CliWorld.Id, "otpauth://x", "KEY", [])));
        right.Mfa.Setup(m => m.CompleteEnrollmentAsync(CliWorld.Id, NewCode, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                right.Events.Add("service:enrolled");
                return ValueTask.FromResult<IReadOnlyList<string>>(CliWorld.NewCodes);
            });
        var ok = right.Console(redirected: false, NewCode);

        (await right.RunAsync(ok, CliWorld.OnAccount("enroll-mfa"))).Should().Be(EmergencyExitCodes.Success);
        ok.Events.IndexOf("out:AAAA-1111").Should().BeGreaterThan(ok.Events.IndexOf("service:enrolled"));
    }

    // ------------------------------------------------------------------------------------------------ regeneration

    [Fact]
    public async Task Regeneration_shows_codes_from_the_committed_result_and_refuses_an_unenrolled_account()
    {
        var w = new CliWorld();
        w.Enrolled(true);
        w.Mfa.Setup(m => m.RegenerateRecoveryCodesAsync(CliWorld.Id, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                w.Events.Add("service:regenerated");
                return ValueTask.FromResult<IReadOnlyList<string>>(CliWorld.NewCodes);
            });
        var console = w.Console(false);

        (await w.RunAsync(console, CliWorld.OnAccount("regenerate-codes"))).Should().Be(EmergencyExitCodes.Success);
        console.Events.IndexOf("out:BBBB-2222").Should().BeGreaterThan(console.Events.IndexOf("service:regenerated"));

        var none = new CliWorld();
        none.Enrolled(false);
        var refused = none.Console(false);
        (await none.RunAsync(refused, CliWorld.OnAccount("regenerate-codes"))).Should().Be(EmergencyExitCodes.Refused);
        refused.ErrorText.ToString().Should().Contain("enroll-mfa");
    }

    [Fact]
    public async Task A_regeneration_whose_outcome_is_unknown_shows_no_codes()
    {
        var w = new CliWorld();
        w.Enrolled(true);
        w.Mfa.Setup(m => m.RegenerateRecoveryCodesAsync(CliWorld.Id, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() => CliWorld.Faulted<IReadOnlyList<string>>(new AuditOutcomeUnknownException("op-5")));
        var console = w.Console(false);

        (await w.RunAsync(console, CliWorld.OnAccount("regenerate-codes"))).Should().Be(EmergencyExitCodes.OutcomeUnknown);
        console.AllOutput.Should().NotContain("will not be shown again");
    }

    // ------------------------------------------------------------------------------------------------ the rest

    [Fact]
    public async Task Revoke_without_confirmation_is_refused_by_the_service_as_a_usage_error_and_with_it_succeeds()
    {
        var w = new CliWorld();
        w.Emergency.Setup(e => e.RevokeAsync(false, It.IsAny<Apperture.Shared.Kernel.ActorId?>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask(Task.FromException(new EmergencyAccessException(EmergencyAccessErrorCodes.ConfirmFallbackRequired, "Confirmation is required."))));
        w.Emergency.Setup(e => e.RevokeAsync(true, It.IsAny<Apperture.Shared.Kernel.ActorId?>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.CompletedTask);

        (await w.RunAsync(w.Console(false), ["revoke"])).Should().Be(EmergencyExitCodes.InvalidUsage);
        (await w.RunAsync(w.Console(false), ["revoke", "--confirm-fallback"])).Should().Be(EmergencyExitCodes.Success);
    }

    [Fact]
    public async Task An_unknown_user_is_invalid_input_and_nothing_else_runs()
    {
        var w = new CliWorld();
        w.Users.Setup(u => u.GetByIdAsync(new Apperture.Core.Domain.Identity.UserId("nobody"), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<Apperture.Core.Domain.Identity.User?>(null));
        var console = w.Console(false);

        (await w.RunAsync(console, ["reset-mfa", "--user-id", "nobody"])).Should().Be(EmergencyExitCodes.InvalidUsage);
        console.ErrorText.ToString().Should().Contain("User not found.");
    }

    [Fact]
    public async Task An_unexpected_failure_is_an_internal_failure_and_prints_no_secret_from_the_exception()
    {
        var w = new CliWorld();
        w.StageReturns();
        w.VerifyAccepts(NewCode);
        w.SwitchThrows(new FormatException("the code 123456 and secret STAGEDSECRETKEY"));
        var console = w.Console(false, NewCode);

        (await w.RunAsync(console, CliWorld.OnAccount("reset-mfa"))).Should().Be(EmergencyExitCodes.InternalFailure);
        console.ErrorText.ToString().Should().NotContain("the code 123456");
        console.ErrorText.ToString().Should().Contain("FormatException");
    }

    [Fact]
    public async Task The_status_command_reads_and_prints_the_posture_and_exits_zero()
    {
        var w = new CliWorld();
        w.Emergency.Setup(e => e.EnsureCanonicalIdentityAsync(AuditActor.EmergencyCli, It.IsAny<CancellationToken>())).Returns(() => ValueTask.CompletedTask);
        w.Emergency.Setup(e => e.GetPostureAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new EmergencyAccessPosture(
                IsHealthy: true,
                FallbackSafeguardRequired: false,
                DesignatedUserId: "usr-1",
                DesignatedDisplayName: "Recovery Access",
                DesignatedIsPlatformAdministrator: true,
                LocalCredentialEnabled: true,
                NativeMfaEnrolled: true,
                UnusedRecoveryCodeCount: 7,
                DesignatedUsername: "breakglass")));
        var console = w.Console(true);

        (await w.RunAsync(console, ["status"])).Should().Be(EmergencyExitCodes.Success);
        console.OutText.ToString().Should().Contain("Authenticator: Enrolled").And.Contain("Recovery codes: 7 available");
    }
}
