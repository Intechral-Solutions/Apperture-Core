using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.UnitTests.AdministratorViability;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-10.2b WP4B-C: the Recovery Access designation and its strict revocation as reducers of administrator viability, and provisioning
/// as a gated raiser, run against the real coordinator and the real guard over scripted states. Each case is about how the operation
/// calls them: the gate first, the decision last and on what the operation's own writes left, a refusal that changes nothing and is
/// recorded with the evidence class the decision used, the account named by the row it loaded, and the transition the operation claims.
/// The same properties against real SQL Server are in the integration suite.
/// </summary>
public sealed partial class EmergencyAccessServiceTests
{
    private static readonly ConfigurationTrust AttestedTrust =
        new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], ConfigurationEvidence.Attested);

    private static readonly ConfigurationTrust NoEvidence =
        new(ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Unknown, ["No Management Host has recorded its configuration."], ConfigurationEvidence.Unavailable);

    /// <summary>Scripted states that log each read to the same list the repositories log their writes to.</summary>
    private sealed class LoggingReads(List<string> log, params AdministratorViabilityState[] states) : IAdministratorViabilityService
    {
        private readonly ScriptedViabilityReads _inner = new(states);

        public ValueTask<AdministratorViabilityState> ReadAsync(CancellationToken cancellationToken = default)
        {
            log.Add("viability read");
            return _inner.ReadAsync(cancellationToken);
        }

        public ValueTask<AdministratorViabilityState> ReadForAttemptAsync(AttemptConfigurationReading attempt, CancellationToken cancellationToken = default) =>
            ReadAsync(cancellationToken);
    }

    /// <summary>A coordinator that lets a test act at the moment the gate has been taken.</summary>
    private sealed class OnEnter(IAdministratorViabilityCoordinator inner, Action entered) : IAdministratorViabilityCoordinator
    {
        public ValueTask PrepareAsync(CancellationToken cancellationToken = default) => inner.PrepareAsync(cancellationToken);

        public async ValueTask<AdministratorViabilityScope> EnterAsync(CancellationToken cancellationToken = default)
        {
            var scope = await inner.EnterAsync(cancellationToken);
            entered();
            return scope;
        }

        public ValueTask<AdministratorGateOnlyScope> EnterGateOnlyAsync(CancellationToken cancellationToken = default) => inner.EnterGateOnlyAsync(cancellationToken);
    }

    private static Harness Rollback(Harness harness)
    {
        harness.Recorder.CaptureState = harness.Users.CaptureState;
        return harness;
    }

    // ------------------------------------------------------------------------------------------------ designation

    [Fact]
    public async Task A_designation_takes_the_gate_and_is_decided_after_every_write_it_makes()
    {
        var harness = Harness.WithAdmin("admin-1");
        harness.Users.Seed(Admin("old-ra", designated: true, username: "oldglass"));
        harness.Credentials.Seed("old-ra", disabled: false);
        var log = new List<string>();
        harness.Users.Log = log;
        harness.UseReads(new LoggingReads(log, ViabilityTestData.Same(["fallback-admin"], ["fallback-admin"])));

        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        harness.Gate.Acquired.Should().Be(1);
        log.Should().Equal(
            ["viability read", "designation old-ra True->False", "designation admin-1 False->True", "rename admin-1", "viability read"],
            "the state before is read under the gate, then every write is made (demotion, designation, rename), and the decision is read last");
    }

    [Fact]
    public async Task A_designation_that_would_leave_no_viable_ordinary_administrator_is_refused_changes_nothing_and_records_the_evidence_it_used()
    {
        var harness = Rollback(Harness.WithAdmin("admin-1"));
        // admin-1 is the only ordinary administrator who can sign in; designated, it no longer counts as one.
        harness.UseViability(
            ViabilityTestData.Same(["admin-1"], ["admin-1"], trust: AttestedTrust),
            ViabilityTestData.Same([], [], trust: AttestedTrust));

        var act = () => harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.LastViableAdministrator);
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeFalse("the refused designation was rolled back");
        harness.Recorder.Actions.Should().BeEmpty("nothing committed");
        var refusal = harness.Events.Written.Should().ContainSingle().Which;
        refusal.Actor.Should().Be(AuditActor.EmergencyCli);
        refusal.BestEffort.Should().BeTrue("the refusal row is written after the rollback, on its own");
        refusal.Event.Action.Should().Be(AuditActionNames.AdministratorViabilityBlocked);
        refusal.Event.Target!.Id.Should().Be("admin-1");
        refusal.Event.Fields["operation"].Should().Be("recovery_access.designate");
        refusal.Event.Fields["code"].Should().Be(GovernanceConflictCodes.LastViableAdministrator);
        refusal.Event.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested, "the refusal names the evidence the decision used");
    }

    [Fact]
    public async Task A_designation_without_trusted_configuration_evidence_is_refused_as_unavailable_with_unavailable_evidence()
    {
        var harness = Rollback(Harness.WithAdmin("admin-1"));
        harness.UseViability(
            ViabilityTestData.Same(["admin-1", "admin-2"], ["admin-1", "admin-2"], trust: NoEvidence),
            ViabilityTestData.Same(["admin-2"], ["admin-2"], trust: NoEvidence));

        var act = () => harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.AdministratorViabilityUnavailable);
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeFalse();
        var refusal = harness.Events.Single(AuditActionNames.AdministratorViabilityBlocked);
        refusal.Fields["code"].Should().Be(GovernanceConflictCodes.AdministratorViabilityUnavailable);
        refusal.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Unavailable);
    }

    [Fact]
    public async Task An_allowed_designation_records_the_evidence_class_of_its_decision()
    {
        var harness = Harness.WithAdmin("admin-1");
        harness.UseViability(ViabilityTestData.Same(["admin-1", "admin-2"], ["admin-1", "admin-2"], trust: AttestedTrust),
            ViabilityTestData.Same(["admin-2"], ["admin-2"], trust: AttestedTrust));

        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);

        harness.Recorder.Single(AuditActionNames.RecoveryAccessDesignated).Fields[ConfigurationEvidenceNames.AuditDetailName]
            .Should().Be(ConfigurationEvidenceNames.Attested);
        harness.Events.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task A_designation_named_with_a_differently_cased_id_writes_decides_and_records_the_stored_account()
    {
        var harness = Rollback(Harness.WithAdmin("admin-1"));
        harness.Users.IgnoreCaseLikeTheDatabase = true;

        await harness.Emergency.DesignateAsync(new UserId("ADMIN-1"), Cli);

        harness.Users.DesignationCalls.Should().Equal([("admin-1", false, true)], "the setter is given the id of the row loaded, not the caller's spelling");
        harness.Recorder.Single(AuditActionNames.RecoveryAccessDesignated).Target!.Id.Should().Be("admin-1");
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();

        // And a refusal names the stored account too.
        var refused = Rollback(Harness.WithAdmin("admin-9"));
        refused.Users.IgnoreCaseLikeTheDatabase = true;
        refused.UseViability(ViabilityTestData.Same(["admin-9"], ["admin-9"], trust: AttestedTrust), ViabilityTestData.Same([], [], trust: AttestedTrust));
        var act = () => refused.Emergency.DesignateAsync(new UserId("Admin-9"), Cli).AsTask();
        await act.Should().ThrowAsync<GovernanceConflictException>();
        refused.Events.Single(AuditActionNames.AdministratorViabilityBlocked).Target!.Id.Should().Be("admin-9");
    }

    [Fact]
    public async Task A_designation_whose_writes_did_not_leave_the_loaded_account_designated_is_an_invariant_failure()
    {
        var harness = Rollback(Harness.WithAdmin("admin-1"));
        harness.Users.PretendNextDesignationChange = true;

        var act = () => harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli).AsTask();

        await act.Should().ThrowAsync<AuditInvariantException>();
        harness.Recorder.Actions.Should().BeEmpty();
        harness.Events.Written.Should().BeEmpty("an invariant failure is an internal error, never a refusal");
    }

    // ------------------------------------------------------------------------------------------------ revocation

    [Fact]
    public async Task A_revocation_takes_the_gate_and_is_decided_after_its_write()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);
        var log = new List<string>();
        harness.Users.Log = log;
        harness.UseReads(new LoggingReads(log, ViabilityTestData.Same(["fallback-admin"], ["fallback-admin"])));

        await harness.Emergency.RevokeAsync(confirmFallback: true, Cli);

        harness.Gate.Acquired.Should().Be(2, "the designation and the revocation each took the gate");
        log.Should().Equal(["viability read", "designation admin-1 True->False", "viability read"]);
    }

    [Fact]
    public async Task A_revocation_without_another_entra_independent_administrator_is_refused_and_recorded_with_its_evidence()
    {
        var harness = Rollback(Harness.WithAdmin("admin-1"));
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);
        // The only administrator who can sign in without Microsoft is the account being revoked: it does not count (C-13-R).
        harness.UseViability(ViabilityTestData.Same(["admin-1"], ["admin-1"], trust: AttestedTrust));

        var act = () => harness.Emergency.RevokeAsync(confirmFallback: true, Cli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue("the refused revocation was rolled back");
        harness.Recorder.Actions.Should().NotContain(AuditActionNames.RecoveryAccessRevoked);
        var refusal = harness.Events.Single(AuditActionNames.AdministratorViabilityBlocked);
        refusal.Target!.Id.Should().Be("admin-1");
        refusal.Fields["operation"].Should().Be("recovery_access.revoke");
        refusal.Fields["code"].Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        refusal.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested);
    }

    [Fact]
    public async Task A_revocation_without_an_established_local_reading_is_refused_as_unavailable()
    {
        var harness = Rollback(Harness.WithAdmin("admin-1"));
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);
        harness.UseViability(ViabilityTestData.Same(["admin-2"], ["admin-2"], trust: NoEvidence));

        var act = () => harness.Emergency.RevokeAsync(confirmFallback: true, Cli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.AdministratorViabilityUnavailable);
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();
        harness.Events.Single(AuditActionNames.AdministratorViabilityBlocked).Fields[ConfigurationEvidenceNames.AuditDetailName]
            .Should().Be(ConfigurationEvidenceNames.Unavailable);
    }

    [Fact]
    public async Task An_allowed_revocation_records_the_evidence_class_of_its_decision()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);
        harness.UseViability(ViabilityTestData.Same(["admin-2"], ["admin-2"], trust: AttestedTrust));

        await harness.Emergency.RevokeAsync(confirmFallback: true, Cli);

        harness.Recorder.Single(AuditActionNames.RecoveryAccessRevoked).Fields[ConfigurationEvidenceNames.AuditDetailName]
            .Should().Be(ConfigurationEvidenceNames.Attested);
    }

    [Fact]
    public async Task A_revocation_that_the_setter_reports_but_that_left_the_account_designated_is_not_reported_as_a_revocation()
    {
        // D-2: the strict rule does not look at the state before, so the operation checks its own transition.
        var harness = Rollback(Harness.WithAdmin("admin-1"));
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);
        harness.Users.PretendNextDesignationChange = true;

        var act = () => harness.Emergency.RevokeAsync(confirmFallback: true, Cli).AsTask();

        await act.Should().ThrowAsync<AuditInvariantException>();
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();
        harness.Recorder.Actions.Should().NotContain(AuditActionNames.RecoveryAccessRevoked);
        harness.Events.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task A_revocation_after_which_another_account_is_designated_is_not_reported_as_a_revocation()
    {
        var harness = Rollback(Harness.WithAdmin("admin-1"));
        harness.Users.Seed(Admin("admin-2", designated: false));
        harness.Credentials.Seed("admin-2", disabled: false);
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);
        harness.Users.AfterDesignationChange = () =>
        {
            harness.Users.AfterDesignationChange = null;
            harness.Users.Seed(harness.Users.Get("admin-2")! with { IsBreakGlassEligible = true });
        };

        var act = () => harness.Emergency.RevokeAsync(confirmFallback: true, Cli).AsTask();

        await act.Should().ThrowAsync<AuditInvariantException>();
        harness.Recorder.Actions.Should().NotContain(AuditActionNames.RecoveryAccessRevoked);
    }

    [Fact]
    public async Task A_revocation_judged_on_states_that_do_not_show_its_own_transition_is_an_invariant_failure()
    {
        var harness = Rollback(Harness.WithAdmin("admin-1"));
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);
        // The facts say a different account was designated before, and that one still is after: not this revocation's states.
        var other = new RecoveryAccessFacts("someone-else", true, true, true, false, 0);
        var admins = new[] { ViabilityTestData.Admin("admin-2", local: true) };
        harness.UseViability(
            ViabilityTestData.FromFacts(admins, recovery: other, trust: AttestedTrust),
            ViabilityTestData.FromFacts(admins, recovery: other, trust: AttestedTrust));

        var act = () => harness.Emergency.RevokeAsync(confirmFallback: true, Cli).AsTask();

        await act.Should().ThrowAsync<AuditInvariantException>();
        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeTrue();
        harness.Events.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task A_revocation_judged_on_its_own_transition_with_another_entra_independent_administrator_stands()
    {
        var harness = Harness.WithAdmin("admin-1");
        await harness.Emergency.DesignateAsync(new UserId("admin-1"), Cli);
        var admins = new[] { ViabilityTestData.Admin("admin-1", local: true, recoveryAccess: true), ViabilityTestData.Admin("admin-2", local: true) };
        var afterAdmins = new[] { ViabilityTestData.Admin("admin-1", local: true), ViabilityTestData.Admin("admin-2", local: true) };
        harness.UseViability(
            ViabilityTestData.FromFacts(admins, recovery: new RecoveryAccessFacts("admin-1", true, true, true, false, 0), trust: AttestedTrust),
            ViabilityTestData.FromFacts(afterAdmins, recovery: ViabilityTestData.Recovery(), trust: AttestedTrust));

        await harness.Emergency.RevokeAsync(confirmFallback: true, Cli);

        harness.Users.Get("admin-1")!.IsBreakGlassEligible.Should().BeFalse();
        harness.Recorder.Single(AuditActionNames.RecoveryAccessRevoked).Target!.Id.Should().Be("admin-1");
    }

    // ------------------------------------------------------------------------------------------------ provisioning

    [Fact]
    public async Task Provisioning_takes_the_gate_and_asserts_that_it_reduced_nothing()
    {
        var harness = Harness.Empty();
        harness.Roles.RoleExists = true;

        await harness.Emergency.ProvisionAsync("quiet-maple-lantern-harbor", Cli);

        harness.Gate.Acquired.Should().Be(1);
        harness.Reads.Reads.Should().Be(2, "the state before the new account and the state after it, for the raise-only assertion");
    }

    [Fact]
    public async Task Provisioning_refuses_when_a_designation_committed_before_the_gate_was_taken()
    {
        var harness = Rollback(Harness.Empty());
        harness.Roles.RoleExists = true;
        harness.Users.Seed(Admin("admin-1", designated: false));
        harness.UseReads(harness.Reads, inner => new OnEnter(inner, () =>
            harness.Users.Seed(harness.Users.Get("admin-1")! with { IsBreakGlassEligible = true })));

        var act = () => harness.Emergency.ProvisionAsync("quiet-maple-lantern-harbor", Cli).AsTask();

        (await act.Should().ThrowAsync<EmergencyAccessException>()).Which.Code.Should().Be(EmergencyAccessErrorCodes.AlreadyDesignated);
        harness.Users.AddAttempts.Should().Be(0, "no account is created beside a designation that exists under the gate");
        harness.Recorder.Actions.Should().BeEmpty();
    }
}
