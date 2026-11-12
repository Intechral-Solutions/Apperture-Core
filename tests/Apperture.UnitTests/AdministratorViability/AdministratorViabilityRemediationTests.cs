using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// IAM-10.2b WP2 review remediation. M-1: a change that left every input the definitions read exactly as it was is allowed without
/// a trusted configuration, and a change to any input is not. M-2: a raise-only writer is held to its own effect on the user it
/// changed, judged under the configuration captured before, not to every difference between two reads of the whole system.
/// </summary>
public sealed class AdministratorViabilityRemediationTests
{
    private static readonly ConfigurationTrust EntraUnknown = new(
        ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Unknown, ["The running sign-in handler cannot be read."]);

    private static readonly ConfigurationTrust LocalUnknown = new(
        ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Trusted, ["LocalAuth:Enabled is not set."]);

    private static AdministratorFacts LocalAdmin(string id) => Admin(id, local: true);

    // ================================================================================================ M-1

    [Fact]
    public void Identical_facts_under_an_untrusted_configuration_are_allowed_because_nothing_a_definition_reads_changed()
    {
        var facts = new[] { LocalAdmin("admin") };
        var pre = FromFacts(facts, trust: EntraUnknown);
        var post = FromFacts(facts, trust: EntraUnknown);

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
        AdministratorViabilityState.InputsUnchanged(pre, post).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_order_in_which_facts_and_tenants_were_read_is_not_a_difference(bool reverse)
    {
        var a = new AdministratorFacts("a", false, true, true, [Tenant, OtherTenant]);
        var b = new AdministratorFacts("b", false, true, true, []);
        var pre = FromFacts([a, b], trust: EntraUnknown);
        var post = FromFacts(
            reverse ? [b, a with { ProvenEntraLinkTenantIds = [OtherTenant, Tenant] }] : [a, b],
            trust: EntraUnknown);

        AdministratorViabilityState.FactsUnchanged(pre, post).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AnyChangeToAFact))]
    public void A_change_to_any_fact_a_definition_reads_is_not_unchanged_and_goes_on_to_the_full_evaluation(
        AdministratorFacts[] after, RecoveryAccessFacts? recoveryAfter, bool localAdministratorRemains)
    {
        // The second administrator signs in through Microsoft only, so the unproven Entra reading matters unless the change
        // leaves a proven Entra-independent administrator behind (WP3 review, M-1): then the change is allowed on that proof alone.
        var before = new[] { LocalAdmin("admin"), new AdministratorFacts("other", false, false, false, [Tenant]) };
        var pre = FromFacts(before, trust: EntraUnknown);
        var post = FromFacts(after, recovery: recoveryAfter, trust: EntraUnknown);

        AdministratorViabilityState.FactsUnchanged(pre, post).Should().BeFalse();
        AdministratorViabilityState.InputsUnchanged(pre, post).Should().BeFalse();
        AdministratorViabilityGuard.ProvesSafeWithoutEntra(post).Should().Be(localAdministratorRemains);
        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(
            localAdministratorRemains ? ViabilityVerdictKind.Allowed : ViabilityVerdictKind.ConfigurationUnavailable);
    }

    public static IEnumerable<object?[]> AnyChangeToAFact()
    {
        var admin = LocalAdmin("admin");
        var other = new AdministratorFacts("other", false, false, false, [Tenant]);
        yield return [new[] { admin with { LocalCredentialEnabled = false }, other }, null, false];
        yield return [new[] { admin with { HasLocalLink = false }, other }, null, false];
        yield return [new[] { admin, other with { ProvenEntraLinkTenantIds = [] } }, null, true];
        yield return [new[] { admin, other with { ProvenEntraLinkTenantIds = [OtherTenant] } }, null, true];
        yield return [new[] { admin with { IsRecoveryAccess = true }, other }, null, false];
        yield return [new[] { admin }, null, true];
        yield return [new[] { admin, other, LocalAdmin("new-holder") }, null, true];
        yield return [new[] { admin, other }, Recovery(healthy: true), true];
    }

    [Fact]
    public void A_state_without_its_facts_is_never_treated_as_unchanged()
    {
        var pre = Same(["a"], ["a"], trust: EntraUnknown);
        var post = Same(["a"], ["a"], trust: EntraUnknown);

        AdministratorViabilityState.FactsUnchanged(pre, post).Should().BeFalse("facts that were not read cannot be compared");
        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_change_of_configuration_with_the_same_facts_is_not_unchanged_so_a_registration_writer_is_still_judged()
    {
        var facts = new[] { new AdministratorFacts("entra", false, false, false, [Tenant]) };
        var pre = FromFacts(facts, now: Availability(ViabilityView.Now, entra: true), restart: Availability(ViabilityView.Restart, entra: true));
        var post = FromFacts(facts, now: Availability(ViabilityView.Now, entra: true), restart: Availability(ViabilityView.Restart, entra: false));

        AdministratorViabilityState.InputsUnchanged(pre, post).Should().BeFalse();
        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator, "the Restart view lost its only way in");
    }

    [Fact]
    public void A_change_of_trust_with_the_same_facts_is_not_unchanged()
    {
        var facts = new[] { LocalAdmin("admin") };

        AdministratorViabilityState.InputsUnchanged(FromFacts(facts), FromFacts(facts, trust: EntraUnknown)).Should().BeFalse();
    }

    [Fact]
    public void An_administrator_reduction_under_an_untrusted_configuration_still_fails_closed()
    {
        var pre = FromFacts([LocalAdmin("admin")], trust: LocalUnknown);
        var post = FromFacts([LocalAdmin("admin") with { LocalCredentialEnabled = false }], trust: LocalUnknown);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public async Task The_service_reads_the_facts_into_the_state_it_returns()
    {
        var facts = new FixedFacts([LocalAdmin("admin")], Recovery(healthy: true));
        var service = new AdministratorViabilityService(facts, new FixedSignInConfiguration());

        var state = await service.ReadAsync();

        state.Administrators.Should().BeEquivalentTo([LocalAdmin("admin")]);
        state.Recovery.Should().Be(Recovery(healthy: true));
    }

    private sealed class FixedFacts(IReadOnlyList<AdministratorFacts> administrators, RecoveryAccessFacts recovery) : IAdministratorFactsReader
    {
        public ValueTask<IReadOnlyList<AdministratorFacts>> ReadAdministratorsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(administrators);

        public ValueTask<RecoveryAccessFacts> ReadRecoveryAccessAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(recovery);
    }

    // ================================================================================================ M-2

    [Fact]
    public void Consuming_the_last_recovery_code_between_the_reads_is_not_the_raisers_doing()
    {
        // Recovery Access is healthy and there is no Entra-independent administrator: the old whole-system assertion saw I-FALLBACK
        // newly violated when the code was consumed, and blamed the writer.
        var recoveryFacts = Recovery(healthy: true);
        var before = FromFacts([], recovery: recoveryFacts);
        var after = FromFacts([LocalAdmin("assigned") with { LocalCredentialEnabled = false }], recovery: Recovery(healthy: false));

        AdministratorViabilityGuard.Detect(before, after).IsAllowed.Should().BeFalse("the whole-system comparison reports a violation");
        RaiseOnlyAttribution.FindReduction(before, after, "assigned").Should().BeNull();
    }

    [Fact]
    public void A_reload_of_the_configuration_between_the_reads_is_judged_under_the_configuration_captured_before()
    {
        var viable = LocalAdmin("viable");
        var target = LocalAdmin("target") with { LocalCredentialEnabled = false };
        var before = FromFacts([viable, target]);
        // After the change the target can sign in, but a reload switched Local sign-in off for everybody.
        var after = FromFacts(
            [viable, target with { LocalCredentialEnabled = true }],
            now: Availability(ViabilityView.Now, local: false),
            restart: Availability(ViabilityView.Restart, local: false));

        AdministratorViabilityGuard.Detect(before, after).IsAllowed.Should().BeFalse("two whole-system evaluations differ");
        RaiseOnlyAttribution.FindReduction(before, after, "target").Should().BeNull();
    }

    [Fact]
    public void A_change_to_another_administrator_is_not_the_raisers_doing()
    {
        var target = new AdministratorFacts("target", false, false, false, []);
        var bystander = LocalAdmin("bystander");
        var before = FromFacts([target, bystander]);
        var after = FromFacts([target with { HasLocalLink = true, LocalCredentialEnabled = true }, bystander with { LocalCredentialEnabled = false }]);

        RaiseOnlyAttribution.FindReduction(before, after, "target").Should().BeNull();
    }

    [Fact]
    public void A_user_who_held_no_administrator_role_before_had_nothing_to_lose()
    {
        var before = FromFacts([LocalAdmin("other")]);
        var after = FromFacts([LocalAdmin("other"), new AdministratorFacts("new", false, false, false, [])]);

        RaiseOnlyAttribution.FindReduction(before, after, "new").Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void A_previously_non_viable_target_who_becomes_viable_or_stays_non_viable_is_not_a_reduction(bool before, bool after)
    {
        var stranded = new AdministratorFacts("target", false, true, before, []);
        var statesBefore = FromFacts([stranded]);
        var statesAfter = FromFacts([stranded with { LocalCredentialEnabled = after }]);

        RaiseOnlyAttribution.FindReduction(statesBefore, statesAfter, "target").Should().BeNull();
    }

    [Fact]
    public void A_designation_between_the_reads_is_not_a_reduction_by_the_raiser()
    {
        var target = LocalAdmin("target");
        var before = FromFacts([target]);
        var after = FromFacts([target with { IsRecoveryAccess = true }]);

        RaiseOnlyAttribution.FindReduction(before, after, "target").Should().BeNull();
    }

    [Theory]
    [InlineData("credential")]
    [InlineData("link")]
    [InlineData("role")]
    public void A_genuine_reduction_of_the_affected_user_is_found(string how)
    {
        var target = LocalAdmin("target");
        var before = FromFacts([target]);
        var after = how switch
        {
            "credential" => FromFacts([target with { LocalCredentialEnabled = false }]),
            "link" => FromFacts([target with { HasLocalLink = false }]),
            _ => FromFacts([]),
        };

        RaiseOnlyAttribution.FindReduction(before, after, "target").Should().Contain("no longer a viable administrator");
    }

    [Fact]
    public void Losing_only_the_entra_independent_path_is_a_reduction_even_though_the_user_still_signs_in()
    {
        var target = new AdministratorFacts("target", false, true, true, [Tenant]);
        var before = FromFacts([target]);
        var after = FromFacts([target with { LocalCredentialEnabled = false }]);

        RaiseOnlyAttribution.FindReduction(before, after, "target").Should().Contain("Entra-independent");
    }

    [Fact]
    public void A_state_built_without_facts_is_compared_by_the_users_membership()
    {
        var before = Same(["target"], ["target"]);

        RaiseOnlyAttribution.FindReduction(before, Same(["target"], ["target"]), "target").Should().BeNull();
        RaiseOnlyAttribution.FindReduction(before, Same([], []), "target").Should().NotBeNull();
        RaiseOnlyAttribution.FindReduction(before, Same(["target"], []), "target").Should().Contain("Entra-independent");
    }

    // ---- the scope ----

    [Fact]
    public async Task The_scope_fails_a_genuine_violation_with_an_audit_invariant_failure_and_not_an_unrelated_difference()
    {
        var target = LocalAdmin("target");
        var reads = new ScriptedViabilityReads(
            FromFacts([target]),
            FromFacts([target with { LocalCredentialEnabled = false }], recovery: Recovery(healthy: true)));
        var scope = await new AdministratorViabilityCoordinator(new RecordingGate(), reads).EnterAsync();
        await scope.CaptureBeforeForRaiseAsync("target");

        var act = () => scope.AssertRaiseOnlyAsync().AsTask();

        (await act.Should().ThrowAsync<AuditInvariantException>()).Which.Message.Should().Contain("reduced it").And.Contain("no longer a viable administrator");
    }

    [Fact]
    public async Task The_scope_passes_a_valid_raise_whatever_the_configuration_trust_after()
    {
        var target = new AdministratorFacts("target", false, true, false, []);
        var reads = new ScriptedViabilityReads(
            FromFacts([target]),
            FromFacts([target with { LocalCredentialEnabled = true }], trust: LocalUnknown));
        var scope = await new AdministratorViabilityCoordinator(new RecordingGate(), reads).EnterAsync();
        await scope.CaptureBeforeForRaiseAsync("target");

        await scope.AssertRaiseOnlyAsync();
    }

    [Fact]
    public async Task A_raise_must_name_its_user_and_a_reducer_cannot_assert_a_raise()
    {
        var scope = await new AdministratorViabilityCoordinator(new RecordingGate(), new ScriptedViabilityReads(FromFacts([]))).EnterAsync();
        await scope.CaptureBeforeAsync();

        var assertAfterReduceCapture = () => scope.AssertRaiseOnlyAsync().AsTask();
        var blank = () => scope.CaptureBeforeForRaiseAsync(" ").AsTask();

        await assertAfterReduceCapture.Should().ThrowAsync<InvalidOperationException>();
        await blank.Should().ThrowAsync<ArgumentException>();
    }
}
