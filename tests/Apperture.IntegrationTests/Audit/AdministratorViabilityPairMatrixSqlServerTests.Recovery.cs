using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP5, A10.2b.11: a Recovery Access or authenticator change racing a reduction of the Entra-independent set gives exactly one
// outcome and never the violating pair; the Recovery Access and authenticator writers against each other in the orders no earlier suite ran;
// and the consumption of a recovery code at sign-in (ruling C-14), which takes no gate, against an administrator writer that holds it.
public sealed partial class AdministratorViabilityPairMatrixSqlServerTests
{
    public static IEnumerable<object[]> RecoveryAccessPairs() =>
        from concurrently in new[] { true, false }
        from rcsi in new[] { false, true }
        from other in new[] { Writer.RemoveRole, Writer.DisableCredential, Writer.RemoveLocalLink, Writer.EntraPrimaryPolicy }
        from recoveryFirst in new[] { true, false }
        select new object[] { other, recoveryFirst, concurrently, rcsi };

    /// <summary>
    /// Healthy Recovery Access (an authenticator enrolled through the service), A the one Entra-independent administrator (who also signs in
    /// through Microsoft), and E who signs in only through Microsoft, so I-VIABLE never decides and only I-FALLBACK does.
    /// </summary>
    private async Task<(World W, string Recovery, string A)> RecoveryWorldAsync(bool rcsi)
    {
        var w = await WorldAsync(rcsi);
        var recovery = await HealthyRecoveryAsync(w);
        var a = await BothWaysAsync(w, "a");
        await EntraOnlyAsync(w, "e");
        var before = await ViabilityAsync(w);
        before.RecoveryHealthy.Should().BeTrue();
        before.Now.EntraIndependentAdministrators.Should().BeEquivalentTo([a], "Recovery Access is never counted");
        return (w, recovery, a);
    }

    private static async Task AssertNeverTheViolatingPairAsync(World w)
    {
        var after = await ViabilityAsync(w);
        foreach (var view in new[] { after.Now, after.Restart })
        {
            view.ViableOrdinaryAdministrators.Should().NotBeEmpty("an ordinary administrator who can sign in remains");
            (after.RecoveryHealthy || view.EntraIndependentAdministrators.Count > 0).Should().BeTrue(
                "Recovery Access is healthy or an administrator signs in without Microsoft: the pair never leaves both lost");
        }
    }

    // ================================================================================================ strict revocation (C-13-R)

    [Theory]
    [MemberData(nameof(RecoveryAccessPairs))]
    public async Task F3_a_strict_revocation_and_a_reduction_of_the_last_entra_independent_administrator_give_the_serial_outcome(Writer other, bool revocationFirst, bool concurrently, bool rcsi)
    {
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        var revoke = new Op(Writer.Revoke, recovery);
        var reduce = new Op(other, a);
        var reduced = other == Writer.EntraPrimaryPolicy ? PolicyDisabled : Applied;

        var (x, y) = revocationFirst ? (revoke, reduce) : (reduce, revoke);
        var outcomes = await PairAsync(w, x, y, concurrently);

        if (revocationFirst)
        {
            // A revocation leaves the former Recovery Access account an ordinary administrator with its Local credential (ruling D-1), so it is
            // the Entra-independent administrator the reduction of A then leaves: both commit, and nothing is lost.
            await AssertPairAsync(w, x, y, outcomes, Applied, reduced);
            (await ViabilityAsync(w)).Now.EntraIndependentAdministrators.Should().BeEquivalentTo([recovery]);
        }
        else
        {
            // With healthy Recovery Access the reduction is allowed on its own. The strict revocation then needs an Entra-independent
            // administrator other than the account it revokes, and there is none: it is refused, as it would be whatever the health was.
            await AssertPairAsync(w, x, y, outcomes, reduced, FallbackRequired);
            (await w.W.DesignatedAsync(recovery)).Should().BeTrue("the refused revocation was rolled back");
        }

        await AssertNeverTheViolatingPairAsync(w);
    }

    // ================================================================================================ the bare reset

    [Theory]
    [MemberData(nameof(RecoveryAccessPairs))]
    public async Task F4_a_bare_reset_of_healthy_recovery_access_and_a_reduction_of_the_last_entra_independent_administrator_commit_once(Writer other, bool resetFirst, bool concurrently, bool rcsi)
    {
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        var reset = new Op(Writer.BareReset, recovery);
        var reduce = new Op(other, a);

        var (x, y) = resetFirst ? (reset, reduce) : (reduce, reset);
        var outcomes = await PairAsync(w, x, y, concurrently);

        // Either change alone leaves I-FALLBACK satisfied (an Entra-independent administrator, or healthy Recovery Access); together they would
        // leave neither, so whichever is decided second is refused, or for the policy deferred with its attempt row.
        if (resetFirst)
            await AssertPairAsync(w, x, y, outcomes, Applied, other == Writer.EntraPrimaryPolicy ? PolicyDeferredFallback : FallbackRequired);
        else
            await AssertPairAsync(w, x, y, outcomes, other == Writer.EntraPrimaryPolicy ? PolicyDisabled : Applied, FallbackRequired);

        if (resetFirst)
        {
            (await w.W.RowsAsync(AuditActionNames.MfaReset, recovery)).Should().ContainSingle()
                .Which.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested, "the reset reduced the posture, so it needed the attested Local reading");
        }

        await AssertNeverTheViolatingPairAsync(w);
    }

    // ================================================================================================ the replacement switch

    [Theory]
    [MemberData(nameof(RecoveryAccessPairs))]
    public async Task F5_a_replacement_switch_and_a_reduction_of_the_last_entra_independent_administrator_both_commit_and_recovery_access_stays_healthy(Writer other, bool switchFirst, bool concurrently, bool rcsi)
    {
        // A switch replaces the authenticator and its codes in one step, so the posture never lapses: it cannot reduce, and a reduction next to
        // it is decided on a healthy posture. Under the gate nothing else moves between the switch's reads, so it is decided on the posture
        // alone and is never refused as administrator_access_changed.
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        await StageVerifiedReplacementAsync(w, recovery);
        var swap = new Op(Writer.Switch, recovery);
        var reduce = new Op(other, a);
        var reduced = other == Writer.EntraPrimaryPolicy ? PolicyDisabled : Applied;

        var (x, y) = switchFirst ? (swap, reduce) : (reduce, swap);
        var outcomes = await PairAsync(w, x, y, concurrently);

        await AssertPairAsync(w, x, y, outcomes, switchFirst ? Applied : reduced, switchFirst ? reduced : Applied);
        var after = await ViabilityAsync(w);
        after.RecoveryHealthy.Should().BeTrue("the posture never lapsed");
        after.Now.EntraIndependentAdministrators.Should().BeEmpty("A was reduced; Recovery Access is never counted");
        (await w.W.RowsAsync(AuditActionNames.MfaReplaced, recovery)).Should().ContainSingle()
            .Which.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.NotRequired, "decided on the posture alone");
        (await w.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, recovery)).Should().BeEmpty();
    }

    // ================================================================================================ Recovery Access and authenticator
    // writers against each other, in the orders no earlier suite ran

    public static IEnumerable<object[]> RecoveryAccessSequences() =>
        from concurrently in new[] { true, false }
        from rcsi in new[] { false, true }
        from sequence in new[]
        {
            "revoke,designate", "reset,designate", "revoke,reset", "reset,revoke", "switch,reset", "reset,switch", "switch,designate",
            "designate,switch", "revoke,switch",
        }
        select new object[] { sequence, concurrently, rcsi };

    [Theory]
    [MemberData(nameof(RecoveryAccessSequences))]
    public async Task F7_two_recovery_access_or_authenticator_changes_apply_in_the_order_they_took_the_gate(string sequence, bool concurrently, bool rcsi)
    {
        // A and Next sign in without Microsoft, so no step needs Recovery Access to stay healthy and each is allowed on what the other
        // committed; the outcome is the serial one, decided on the committed state (a reset of an account that is no longer Recovery Access
        // needs no evidence; a switch after a reset has nothing to replace).
        var w = await WorldAsync(rcsi);
        var recovery = await HealthyRecoveryAsync(w);
        await BothWaysAsync(w, "a");
        var next = await BothWaysAsync(w, "next");
        if (sequence.Contains("switch", StringComparison.Ordinal))
            await StageVerifiedReplacementAsync(w, recovery);

        Op Of(string step) => step switch
        {
            "revoke" => new Op(Writer.Revoke, recovery),
            "designate" => new Op(Writer.Designate, next),
            "reset" => new Op(Writer.BareReset, recovery),
            "switch" => new Op(Writer.Switch, recovery),
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };
        var steps = sequence.Split(',');
        var outcomes = await PairAsync(w, Of(steps[0]), Of(steps[1]), concurrently);

        outcomes.First.Should().Be(Applied);
        outcomes.Second.Should().Be(
            sequence == "reset,switch" ? "refused:" + GovernanceConflictCodes.AuthenticatorNotEnrolled : Applied,
            "the second change is decided on what the first committed");
        (await w.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, recovery)).Should().BeEmpty("nothing here is a viability refusal");
        (await w.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, next)).Should().BeEmpty();

        var designated = await w.W.DesignatedAsync(recovery);
        var enrolled = await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{recovery}';") == 1;
        var credentialId = enrolled ? await w.Scratch.ScalarAsync<string>($"SELECT CredentialId FROM UserTotpCredentials WHERE UserId = N'{recovery}';") : null;
        async Task<string?> ResetEvidenceAsync() =>
            (await w.W.RowsAsync(AuditActionNames.MfaReset, recovery)).Should().ContainSingle().Which.Fields[ConfigurationEvidenceNames.AuditDetailName];

        switch (sequence)
        {
            case "revoke,designate":
                designated.Should().BeFalse();
                (await w.W.DesignatedAsync(next)).Should().BeTrue();
                break;
            case "reset,designate":
                enrolled.Should().BeFalse();
                designated.Should().BeFalse("the designation moved Recovery Access to the other account");
                (await w.W.DesignatedAsync(next)).Should().BeTrue();
                (await ResetEvidenceAsync()).Should().Be(ConfigurationEvidenceNames.Attested, "the reset reduced a healthy posture");
                break;
            case "revoke,reset":
                designated.Should().BeFalse();
                enrolled.Should().BeFalse();
                (await ResetEvidenceAsync()).Should().Be(ConfigurationEvidenceNames.NotRequired, "decided on the committed revocation: not Recovery Access any more");
                break;
            case "reset,revoke":
                designated.Should().BeFalse();
                enrolled.Should().BeFalse();
                (await ResetEvidenceAsync()).Should().Be(ConfigurationEvidenceNames.Attested);
                break;
            case "switch,reset":
                enrolled.Should().BeFalse("the reset removed the authenticator the switch had made active");
                (await w.W.RowsAsync(AuditActionNames.MfaReplaced, recovery)).Should().ContainSingle();
                (await ResetEvidenceAsync()).Should().Be(ConfigurationEvidenceNames.Attested, "the switch left the posture healthy, and the reset reduced it");
                break;
            case "reset,switch":
                enrolled.Should().BeFalse();
                (await w.W.RowsAsync(AuditActionNames.MfaReplaced, recovery)).Should().BeEmpty("the switch has nothing to replace and never falls back to an enrolment");
                break;
            case "switch,designate":
            case "designate,switch":
                credentialId.Should().Be(w.Candidate!.Value.CandidateId);
                designated.Should().BeFalse();
                (await w.W.DesignatedAsync(next)).Should().BeTrue();
                break;
            case "revoke,switch":
                designated.Should().BeFalse();
                credentialId.Should().Be(w.Candidate!.Value.CandidateId, "an ordinary account's authenticator can still be replaced");
                break;
        }

        (await w.W.DesignatedCountAsync()).Should().BeLessThanOrEqualTo(1, "the designation is never held by two accounts");
        await AssertNeverTheViolatingPairAsync(w);
    }

    // ================================================================================================ C-14: a recovery code consumed at
    // sign-in takes no gate

    /// <summary>Signs in with each of the account's codes but the last, through the service, so exactly one unused code remains.</summary>
    private static async Task<string> SpendAllButTheLastCodeAsync(World w, string recovery)
    {
        foreach (var code in w.RecoveryCodes.Take(w.RecoveryCodes.Count - 1))
            (await ConsumeAsync(w, recovery, code)).Should().BeTrue();
        (await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId = N'{recovery}' AND ConsumedAtUtc IS NULL;")).Should().Be(1);
        return w.RecoveryCodes[^1];
    }

    private static async Task<bool> ConsumeAsync(World w, string recovery, string code)
    {
        await using var db = w.Scratch.NewDb();
        return await w.W.Mfa(db, time: w.Clock).ConsumeRecoveryCodeAsync(new UserId(recovery), code);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task F8_the_last_recovery_code_consumed_while_a_disable_holds_the_gate_is_not_held_up_and_leaves_the_serial_result_disable_then_sign_in(bool rcsi)
    {
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        var last = await SpendAllButTheLastCodeAsync(w, recovery);

        // The disable has decided (on a healthy posture) and holds the gate; the sign-in with the last code runs and finishes meanwhile.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var disable = Task.Run(() => RunAsync(w, new Op(Writer.DisableCredential, a), pause));
        await ReachedAsync(pause, disable);
        (await ProbeGateAsync(w.Scratch)).Should().Be(-1);
        (await ConsumeAsync(w, recovery, last).WaitAsync(Patience)).Should().BeTrue("a legitimate sign-in is never refused (ruling C-14)");
        (await ProbeGateAsync(w.Scratch)).Should().Be(-1, "the sign-in finished while the disable still held the gate: it takes no gate");
        pause.Release();
        (await disable.WaitAsync(Patience)).Should().Be(Applied, "it was decided before the code was consumed");

        // The state the serial order 'disable, then the sign-in' leaves: not refusable, audited, and surfaced by the posture read on demand.
        var after = await ViabilityAsync(w);
        after.RecoveryHealthy.Should().BeFalse("the last code was consumed");
        after.Now.EntraIndependentAdministrators.Should().BeEmpty();
        (await w.R.AuditRowsAsync(AuditActionNames.CredentialDisabled, a)).Should().Be(1);
        (await w.R.AuditRowsAsync(AuditActionNames.RecoveryCodeConsumed, recovery)).Should().Be(w.RecoveryCodes.Count);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task F8_the_last_recovery_code_consumed_between_a_disables_two_reads_makes_it_refuse_as_the_serial_order_sign_in_then_disable_does(bool rcsi)
    {
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        var last = await SpendAllButTheLastCodeAsync(w, recovery);

        // Parked after the state before (read under the gate on a healthy posture) and before its write.
        var pause = PauseAt.BeforeStatement("UPDATE [LocalCredentials]");
        var disable = Task.Run(() => RunAsync(w, new Op(Writer.DisableCredential, a), pause));
        await ReachedAsync(pause, disable);
        (await ProbeGateAsync(w.Scratch)).Should().Be(-1);
        (await ConsumeAsync(w, recovery, last).WaitAsync(Patience)).Should().BeTrue();
        pause.Release();

        (await disable.WaitAsync(Patience)).Should().Be(FallbackRequired,
            "the state its own change produced has neither an Entra-independent administrator nor healthy Recovery Access: a conservative refusal, the result of the serial order 'sign-in, then disable'");
        (await w.R.CredentialDisabledAsync(a)).Should().BeFalse("the refused disable was rolled back");
        (await w.R.AuditRowsAsync(AuditActionNames.BlockedLastAdminCredentialDisabled, a)).Should().Be(1);
        (await w.R.AuditRowsAsync(AuditActionNames.CredentialDisabled, a)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task F8_after_the_last_recovery_code_is_consumed_a_disable_that_leaves_no_entra_independent_administrator_is_refused(bool rcsi)
    {
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        var last = await SpendAllButTheLastCodeAsync(w, recovery);

        (await ConsumeAsync(w, recovery, last)).Should().BeTrue();
        (await RunAsync(w, new Op(Writer.DisableCredential, a))).Should().Be(FallbackRequired, "every later covered mutation is governed by I-FALLBACK (ruling C-14)");
        (await w.R.CredentialDisabledAsync(a)).Should().BeFalse();
    }

    // ================================================================================================ control: the gate removed

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Control_without_the_gate_a_bare_reset_and_the_disable_of_the_last_entra_independent_administrator_are_not_serialized(bool rcsi)
    {
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        var noGate = new AdministratorViabilityWp2SqlServerTests.NoGate();

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var reset = Task.Run(() => RunAsync(w, new Op(Writer.BareReset, recovery), pause, noGate));
        await ReachedAsync(pause, reset);
        (await ProbeGateAsync(w.Scratch)).Should().BeGreaterThanOrEqualTo(0, "this reset never took the gate");

        var disable = Task.Run(() => RunAsync(w, new Op(Writer.DisableCredential, a), gate: noGate));
        if (rcsi)
        {
            // Under row versioning nothing stands in the disable's way: it reads the committed past (Recovery Access healthy), decides and commits.
            (await disable.WaitAsync(Patience)).Should().Be(Applied);
            pause.Release();
            (await reset.WaitAsync(Patience)).Should().Be(Applied);
            var after = await ViabilityAsync(w);
            after.RecoveryHealthy.Should().BeFalse();
            after.Now.EntraIndependentAdministrators.Should().BeEmpty("both were allowed: this is the pair the gate excludes");
        }
        else
        {
            // Under locking READ COMMITTED the disable's reads wait on the reset's uncommitted rows: a row lock, which protects by accident
            // (it depends on what the writers happen to read and write) and is not the gate.
            var deadline = DateTime.UtcNow.Add(Patience);
            while (await RowLockWaitersAsync(w.Scratch) == 0)
            {
                disable.IsCompleted.Should().BeFalse("the disable must wait on a row");
                DateTime.UtcNow.Should().BeBefore(deadline);
                await Task.Delay(25);
            }

            (await ApplicationLockWaitersAsync(w.Scratch)).Should().Be(0, "it waits on a row, not on the administrator-set gate");
            pause.Release();
            (await reset.WaitAsync(Patience)).Should().Be(Applied);
            (await disable.WaitAsync(Patience)).Should().Be(FallbackRequired);
        }
    }

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };
}
