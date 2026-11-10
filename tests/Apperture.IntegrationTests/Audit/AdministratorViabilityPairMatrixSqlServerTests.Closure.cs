using Apperture.Cli.Host;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP5: acceptance clauses that no earlier package ran against SQL Server. A10.2b.12 end to end (the last recovery code used at a
// sign-in that takes no gate, the presentation of the exhausted codes, I-FALLBACK afterwards, regeneration restoring health); C-14 against
// the switch through the services; A10.2b.5 for the raisers that had only structural evidence (setting a password, proving a link);
// A10.2b.10 for "every" administrator, not only one; and, per family, the outcome-level controls with the gate removed.
public sealed partial class AdministratorViabilityPairMatrixSqlServerTests
{
    // ================================================================================================ A10.2b.12 (ruling C-14)

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A12_the_last_recovery_code_is_used_without_the_gate_the_status_says_exhausted_a_reduction_is_refused_and_regeneration_restores_health(bool rcsi)
    {
        var (w, recovery, a) = await RecoveryWorldAsync(rcsi);
        var last = await SpendAllButTheLastCodeAsync(w, recovery);

        // Never blocked and no gate: the sign-in with the last code finishes while another session holds the administrator-set gate.
        await using (var holder = await HoldTheGateAsync(w.Scratch))
        {
            (await ConsumeAsync(w, recovery, last).WaitAsync(Patience)).Should().BeTrue("a legitimate sign-in is never refused (ruling C-14)");
            (await ProbeGateAsync(w.Scratch)).Should().Be(-1, "the gate was held throughout");
        }

        (await w.R.AuditRowsAsync(AuditActionNames.RecoveryCodeConsumed, recovery)).Should().Be(w.RecoveryCodes.Count, "every use, the last included, is audited");

        // The posture is computed on read, so it is unhealthy at once.
        await using (var db = w.Scratch.NewDb())
        {
            var posture = await w.W.Emergency(db).GetPostureAsync();
            posture.IsHealthy.Should().BeFalse();
            posture.NativeMfaEnrolled.Should().BeTrue();
            posture.UnusedRecoveryCodeCount.Should().Be(0);
        }

        // The presentation, through the command line's own composition, says the codes are exhausted and names the remedy.
        var (statusExit, status) = await CommandLineAsync(w, ["status"]);
        statusExit.Should().Be(EmergencyExitCodes.Success, status.Error.ToString());
        status.Out.ToString().Should().Contain("Healthy: False").And.Contain("Recovery codes: 0 available")
            .And.Contain("Recovery codes are exhausted").And.Contain("Run 'emergency regenerate-codes'");

        // Every covered administrative mutation is now evaluated under I-FALLBACK: reducing the last Entra-independent administrator is refused.
        (await RunAsync(w, new Op(Writer.DisableCredential, a))).Should().Be(FallbackRequired);
        (await w.R.CredentialDisabledAsync(a)).Should().BeFalse();
        (await w.R.AuditRowsAsync(AuditActionNames.BlockedLastAdminCredentialDisabled, a)).Should().Be(1);

        // Regeneration needs no healthy posture and still succeeds through the command line.
        var generatedBefore = await w.R.AuditRowsAsync(AuditActionNames.RecoveryCodesGenerated, recovery);
        var (regenerateExit, regenerate) = await CommandLineAsync(w, ["regenerate-codes"]);
        regenerateExit.Should().Be(EmergencyExitCodes.Success, regenerate.Error.ToString());
        regenerate.Out.ToString().Should().Contain("Recovery codes regenerated");
        (await w.R.AuditRowsAsync(AuditActionNames.RecoveryCodesGenerated, recovery)).Should().Be(generatedBefore + 1);

        // It restores a healthy posture, since the other inputs are healthy, and the status no longer says the codes are exhausted.
        await using (var db = w.Scratch.NewDb())
        {
            var posture = await w.W.Emergency(db).GetPostureAsync();
            posture.IsHealthy.Should().BeTrue();
            posture.UnusedRecoveryCodeCount.Should().BeGreaterThan(0);
        }

        var (_, healthy) = await CommandLineAsync(w, ["status"]);
        healthy.Out.ToString().Should().Contain("Healthy: True").And.NotContain("exhausted");

        // The same reduction is now allowed: healthy Recovery Access is the fallback again.
        (await RunAsync(w, new Op(Writer.DisableCredential, a))).Should().Be(Applied);
        (await w.R.CredentialDisabledAsync(a)).Should().BeTrue();
    }

    // ================================================================================================ C-14 against the switch, through the services

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task C14_a_recovery_code_used_while_a_switch_holds_the_gate_never_asks_for_the_gate_and_is_no_longer_valid_once_the_switch_commits(bool rcsi)
    {
        var (w, recovery, _) = await RecoveryWorldAsync(rcsi);
        await StageVerifiedReplacementAsync(w, recovery);
        var oldCode = w.RecoveryCodes[0];

        // The switch has decided and replaced the codes, and holds the gate and those rows until its commit.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var switching = Task.Run(() => RunAsync(w, new Op(Writer.Switch, recovery), pause));
        await ReachedAsync(pause, switching);
        (await ProbeGateAsync(w.Scratch)).Should().Be(-1, "the switch holds the gate");

        // The sign-in asks for no gate (C-14). If it waits at all, it waits on a code row the switch replaced, never on the application lock.
        var signIn = Task.Run(() => ConsumeAsync(w, recovery, oldCode));
        var deadline = DateTime.UtcNow.Add(Patience);
        while (!signIn.IsCompleted && await RowLockWaitersAsync(w.Scratch) == 0)
        {
            DateTime.UtcNow.Should().BeBefore(deadline);
            await Task.Delay(25);
        }

        (await ApplicationLockWaitersAsync(w.Scratch)).Should().Be(0, "a recovery-code sign-in takes no gate");
        pause.Release();

        (await switching.WaitAsync(Patience)).Should().Be(Applied);
        (await signIn.WaitAsync(Patience)).Should().BeFalse("the code belonged to the set the switch replaced: the serial order 'switch, then sign-in'");
        (await w.R.AuditRowsAsync(AuditActionNames.RecoveryCodeConsumed, recovery)).Should().Be(0, "nothing was consumed");
        (await ViabilityAsync(w)).RecoveryHealthy.Should().BeTrue("the switch issued a new set");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task C14_a_recovery_code_used_before_a_switch_is_consumed_and_audited_and_the_switch_then_replaces_the_set(bool rcsi)
    {
        var (w, recovery, _) = await RecoveryWorldAsync(rcsi);
        await StageVerifiedReplacementAsync(w, recovery);

        (await ConsumeAsync(w, recovery, w.RecoveryCodes[0])).Should().BeTrue();
        (await RunAsync(w, new Op(Writer.Switch, recovery))).Should().Be(Applied, "a consumed code changes nothing the switch decides on");

        (await w.R.AuditRowsAsync(AuditActionNames.RecoveryCodeConsumed, recovery)).Should().Be(1);
        (await ViabilityAsync(w)).RecoveryHealthy.Should().BeTrue();
        (await ConsumeAsync(w, recovery, w.RecoveryCodes[1])).Should().BeFalse("the switch replaced the whole set");
    }

    // ================================================================================================ A10.2b.5: raisers are never refused

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A5_setting_a_password_and_proving_a_link_are_never_refused_even_for_the_last_entra_independent_administrator(bool rcsi)
    {
        var w = await WorldAsync(rcsi);
        await UnhealthyRecoveryAsync(w);
        var last = await w.W.EntraIndependentAdminAsync("last");
        var objectId = Guid.NewGuid().ToString("D");
        await w.Scratch.ExecuteAsync($"""
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt, LastAuthenticatedAt)
            VALUES (N'P{last}', N'{last}', 0, N'{objectId}', N'{Tenant}', SYSDATETIMEOFFSET(), NULL);
            """);
        var before = await ViabilityAsync(w);
        before.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([last]);
        before.Now.EntraIndependentAdministrators.Should().BeEquivalentTo([last]);
        before.RecoveryHealthy.Should().BeFalse("so any reduction of this administrator would be refused");
        var refusalsBefore = await ActionRowsAsync(w, AuditActionNames.AdministratorViabilityBlocked);

        // A password set by the Management Host and by the command line: neither can reduce anything, so neither is refused.
        await using (var db = w.Scratch.NewDb())
            await HostAdministration(w, db, gate: null).SetLocalPasswordAsync(new UserId(last), NewPassword);
        await using (var db = w.Scratch.NewDb())
            await w.W.Emergency(db).SetLocalPasswordAsync(new UserId(last), NewPassword + "-renewed", Cli);
        (await w.R.AuditRowsAsync(AuditActionNames.PasswordSet, last)).Should().Be(2);

        // Proving the pending Microsoft link at sign-in (E-2, no gate): never refused; the sign-in resolves to the administrator.
        var signedIn = await SignInAsync(w, new HumanActor(new ActorId(last), "Last", IdentitySource.Entra, tenantId: Tenant, entraObjectId: objectId));
        signedIn.Should().Be(new UserId(last));
        (await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM IdentityLinks WHERE Id = N'P{last}' AND LastAuthenticatedAt IS NOT NULL;"))
            .Should().Be(1, "the link was proven");

        // Nothing was refused. The policy that runs after the proof keeps the credential of the last Entra-independent administrator (A10.2b.10).
        (await ActionRowsAsync(w, AuditActionNames.AdministratorViabilityBlocked)).Should().Be(refusalsBefore);
        foreach (var action in new[] { AuditActionNames.BlockedLastAdminCredentialDisabled, AuditActionNames.BlockedLastPlatformAdminRemoval })
            (await w.R.AuditRowsAsync(action, last)).Should().Be(0);
        (await w.R.CredentialDisabledAsync(last)).Should().BeFalse();
        var after = await ViabilityAsync(w);
        after.Now.EntraIndependentAdministrators.Should().BeEquivalentTo([last]);
    }

    // ================================================================================================ A10.2b.10: the IAM-3 transition rule

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A10_with_healthy_recovery_access_the_policy_disables_the_local_credential_of_every_entra_proven_administrator_including_the_last(bool rcsi)
    {
        var w = await WorldAsync(rcsi);
        await HealthyRecoveryAsync(w);
        string[] admins = [await BothWaysAsync(w, "first"), await BothWaysAsync(w, "second"), await BothWaysAsync(w, "third")];

        foreach (var id in admins)
            (await RunAsync(w, new Op(Writer.EntraPrimaryPolicy, id))).Should().Be(PolicyDisabled, "healthy Recovery Access is the fallback for each, the last included");

        foreach (var id in admins)
        {
            (await w.R.CredentialDisabledAsync(id)).Should().BeTrue();
            (await w.R.AuditRowsAsync(AuditActionNames.LocalDisabledEntraPrimary, id)).Should().Be(1);
            (await w.R.AuditRowsAsync(AuditActionNames.LocalDisableDeferredLastAdmin, id)).Should().Be(0);
        }

        var after = await ViabilityAsync(w);
        after.Now.EntraIndependentAdministrators.Should().BeEmpty();
        after.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo(admins, "each still signs in through Microsoft");
        after.RecoveryHealthy.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A10_with_recovery_access_not_healthy_the_policy_disables_all_but_the_last_entra_independent_administrator_and_defers_that_one(bool rcsi)
    {
        var w = await WorldAsync(rcsi);
        await UnhealthyRecoveryAsync(w);
        string[] admins = [await BothWaysAsync(w, "first"), await BothWaysAsync(w, "second"), await BothWaysAsync(w, "third")];

        (await RunAsync(w, new Op(Writer.EntraPrimaryPolicy, admins[0]))).Should().Be(PolicyDisabled);
        (await RunAsync(w, new Op(Writer.EntraPrimaryPolicy, admins[1]))).Should().Be(PolicyDisabled);
        (await RunAsync(w, new Op(Writer.EntraPrimaryPolicy, admins[2]))).Should().Be(PolicyDeferredFallback, "the existing outcome for the last one");

        (await w.R.CredentialDisabledAsync(admins[2])).Should().BeFalse();
        (await w.R.AuditRowsAsync(AuditActionNames.LocalDisableDeferredLastAdmin, admins[2])).Should().Be(1, "the existing event");
        (await w.R.AuditRowsAsync(AuditActionNames.LocalDisabledEntraPrimary, admins[2])).Should().Be(0);
        (await ViabilityAsync(w)).Now.EntraIndependentAdministrators.Should().BeEquivalentTo([admins[2]]);
    }

    // ================================================================================================ controls: the gate removed, by family

    public enum NoGateFamily
    {
        TwoDisables,
        DesignationThenDisable,
        DisableThenRevocation,
        PolicyThenDisable,
    }

    public static IEnumerable<object[]> NoGateFamilies() => Enum.GetValues<NoGateFamily>().Select(f => new object[] { f });

    /// <summary>
    /// The outcome-level evidence that the gate is what serializes each family (pre-hand-over review I1). The concurrent cases detect a removed
    /// gate through the harness, which requires the parked writer to hold it; this shows what the writers themselves would then do. Under
    /// READ_COMMITTED_SNAPSHOT the second writer reads the committed past, is allowed, and commits beside the parked first: the pair the gate
    /// excludes. (Under locking READ COMMITTED a row lock often stands in by accident; the existing control shows that for one pair.)
    /// </summary>
    [Theory]
    [MemberData(nameof(NoGateFamilies))]
    public async Task Control_without_the_gate_under_row_versioning_each_familys_pair_commits_twice_and_leaves_the_violation_the_gate_excludes(NoGateFamily family)
    {
        var w = await WorldAsync(rcsi: true);
        var noGate = new AdministratorViabilityWp2SqlServerTests.NoGate();
        Op first;
        Op second;
        string? revoked = null;
        switch (family)
        {
            case NoGateFamily.TwoDisables:
            {
                await UnhealthyRecoveryAsync(w);
                var a = await BothWaysAsync(w, "a");
                var b = await BothWaysAsync(w, "b");
                await EntraOnlyAsync(w, "e");
                (first, second) = (new Op(Writer.DisableCredential, a), new Op(Writer.DisableCredential, b));
                break;
            }

            case NoGateFamily.DesignationThenDisable:
            {
                var a = await BothWaysAsync(w, "a");
                var b = await BothWaysAsync(w, "b");
                await EntraOnlyAsync(w, "e");
                (first, second) = (new Op(Writer.Designate, a), new Op(Writer.DisableCredential, b));
                break;
            }

            case NoGateFamily.DisableThenRevocation:
            {
                var recovery = await HealthyRecoveryAsync(w);
                var a = await BothWaysAsync(w, "a");
                await EntraOnlyAsync(w, "e");
                (first, second) = (new Op(Writer.DisableCredential, a), new Op(Writer.Revoke, recovery));
                revoked = recovery;
                break;
            }

            default:
            {
                await UnhealthyRecoveryAsync(w);
                var a = await BothWaysAsync(w, "a");
                var b = await BothWaysAsync(w, "b");
                await EntraOnlyAsync(w, "e");
                (first, second) = (new Op(Writer.EntraPrimaryPolicy, a), new Op(Writer.DisableCredential, b));
                break;
            }
        }

        // The first is parked after its decision and its write, before its commit, holding its row locks and no gate.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var firstRun = Task.Run(() => RunAsync(w, first, pause, noGate));
        await ReachedAsync(pause, firstRun);
        (await ProbeGateAsync(w.Scratch)).Should().BeGreaterThanOrEqualTo(0, "the first writer never took the gate");

        var secondOutcome = await RunAsync(w, second, gate: noGate).WaitAsync(Patience);
        pause.Release();
        var firstOutcome = await firstRun.WaitAsync(Patience);

        IsApplied(firstOutcome).Should().BeTrue(firstOutcome);
        IsApplied(secondOutcome).Should().BeTrue($"{secondOutcome}: it read the committed past, before the first's change, and was allowed on it");
        var after = await ViabilityAsync(w);
        after.RecoveryHealthy.Should().BeFalse();
        if (revoked is null)
        {
            after.Now.EntraIndependentAdministrators.Should().BeEmpty("both changes were allowed: the state the gate exists to exclude");
        }
        else
        {
            // The revoked account keeps its role and credential and counts again as an ordinary administrator (D-1); what C-13-R excludes
            // is a revocation that leaves no Entra-independent administrator other than that account.
            after.Now.EntraIndependentAdministrators.Should().BeEquivalentTo(
                [revoked], "both changes were allowed: no Entra-independent administrator other than the revoked account, the state C-13-R excludes");
        }
    }
}
