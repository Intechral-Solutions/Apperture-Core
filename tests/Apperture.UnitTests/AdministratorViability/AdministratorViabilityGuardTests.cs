using Apperture.ControlPlane.Security.AdministratorViability;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// I-VIABLE and I-FALLBACK as predicates over a state before and after (IAM-10 gate §3.2, Amendment A; A10.2b.1, .3 to .5, .8,
/// .10), and the strict rule for revoking Recovery Access (C-13-R). Each is applied to the Now and the Restart view independently.
/// </summary>
public sealed class AdministratorViabilityGuardTests
{
    private static readonly ConfigurationTrust LocalUnknown = new(
        ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Trusted, ["Set LocalAuth:Enabled."]);

    private static readonly ConfigurationTrust EntraUnknown = new(
        ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Unknown, ["The secret cannot be established."]);

    // ----- I-VIABLE -----

    [Fact]
    public void Eliminating_the_last_viable_administrator_is_refused()
    {
        var verdict = AdministratorViabilityGuard.Evaluate(Same(["a"], ["a"]), Same([]));

        verdict.Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator);
        verdict.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Reducing_two_viable_administrators_to_one_is_allowed_and_the_second_reduction_is_refused()
    {
        var two = Same(["a", "b"], ["a", "b"]);
        var one = Same(["b"], ["b"]);

        AdministratorViabilityGuard.Evaluate(two, one).IsAllowed.Should().BeTrue("the first reduction leaves one");
        AdministratorViabilityGuard.Evaluate(one, Same([])).Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator);
    }

    [Fact]
    public void An_already_empty_administrator_set_never_refuses_a_repairing_or_unrelated_change()
    {
        AdministratorViabilityGuard.Evaluate(Same([]), Same([])).IsAllowed.Should().BeTrue();
        AdministratorViabilityGuard.Evaluate(Same([]), Same(["a"], ["a"])).IsAllowed.Should().BeTrue("a repair is never refused");
    }

    [Fact]
    public void A_violation_in_either_view_alone_refuses_the_change()
    {
        var pre = State(
            Snapshot(ViabilityView.Now, ["a"], ["a"]),
            Snapshot(ViabilityView.Restart, ["a"], ["a"]));

        var restartOnly = State(
            Snapshot(ViabilityView.Now, ["a"], ["a"]),
            Snapshot(ViabilityView.Restart, []));
        var nowOnly = State(
            Snapshot(ViabilityView.Now, []),
            Snapshot(ViabilityView.Restart, ["a"], ["a"]));

        var restart = AdministratorViabilityGuard.Evaluate(pre, restartOnly);
        restart.Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator);
        restart.View.Should().Be(ViabilityView.Restart);

        var now = AdministratorViabilityGuard.Evaluate(pre, nowOnly);
        now.Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator);
        now.View.Should().Be(ViabilityView.Now);
    }

    [Fact]
    public void A_view_that_was_already_violated_does_not_excuse_or_cause_a_refusal_in_the_other()
    {
        // Restart is already empty before the change: only the Now view is judged for a refusal.
        var pre = State(Snapshot(ViabilityView.Now, ["a"], ["a"]), Snapshot(ViabilityView.Restart, []));
        var post = State(Snapshot(ViabilityView.Now, ["a"], ["a"]), Snapshot(ViabilityView.Restart, []));

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
    }

    // ----- I-FALLBACK -----

    [Fact]
    public void Without_healthy_recovery_access_eliminating_the_last_entra_independent_administrator_is_refused()
    {
        var pre = Same(["a", "b"], ["a"]);
        var post = Same(["b"], []);

        var verdict = AdministratorViabilityGuard.Evaluate(pre, post);

        verdict.Kind.Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired);
    }

    [Fact]
    public void With_two_entra_independent_administrators_the_first_reduction_is_allowed_and_the_second_refused()
    {
        var two = Same(["a", "b"], ["a", "b"]);
        var one = Same(["a", "b"], ["b"]);
        var none = Same(["a", "b"], []);

        AdministratorViabilityGuard.Evaluate(two, one).IsAllowed.Should().BeTrue();
        AdministratorViabilityGuard.Evaluate(one, none).Kind.Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired);
    }

    [Fact]
    public void While_recovery_access_is_demonstrably_healthy_the_fallback_invariant_never_refuses()
    {
        var pre = Same(["a"], ["a"], recoveryHealthy: true);
        var post = Same(["a"], [], recoveryHealthy: true);

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue(
            "the IAM-3 transition rule: healthy Recovery Access lets ordinary administrators become Entra-primary");
    }

    [Fact]
    public void A_change_that_makes_recovery_access_unhealthy_while_no_entra_independent_administrator_exists_is_refused()
    {
        var pre = Same(["a"], [], recoveryHealthy: true);
        var post = Same(["a"], [], recoveryHealthy: false);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired);
    }

    [Fact]
    public void A_change_that_makes_recovery_access_unhealthy_while_an_entra_independent_administrator_exists_is_allowed()
    {
        var pre = Same(["a"], ["a"], recoveryHealthy: true);
        var post = Same(["a"], ["a"], recoveryHealthy: false);

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void A_state_that_already_violates_the_fallback_invariant_is_not_a_reason_to_refuse()
    {
        var pre = Same(["a"], [], recoveryHealthy: false);
        var post = Same(["a", "b"], [], recoveryHealthy: false);

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
    }

    // ----- Raise-only mutations -----

    [Fact]
    public void A_mutation_that_only_adds_viability_is_never_refused_by_either_invariant()
    {
        var pre = Same(["a"], ["a"]);
        var post = Same(["a", "b"], ["a", "b"], recoveryHealthy: true);

        AdministratorViabilityGuard.Detect(pre, post).IsAllowed.Should().BeTrue();
        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Detect_ignores_the_configuration_trust_so_a_raise_only_writer_never_fails_closed()
    {
        var pre = Same([], [], trust: LocalUnknown);
        var post = Same(["a"], ["a"], trust: LocalUnknown);

        AdministratorViabilityGuard.Detect(pre, post).IsAllowed.Should().BeTrue();
    }

    // ----- Fail closed (D-6) -----

    [Fact]
    public void An_unknown_local_setting_fails_closed_for_a_reducing_operation_with_an_actionable_message()
    {
        var pre = Same(["a"], ["a"], trust: LocalUnknown);
        var post = Same(["a", "b"], ["a", "b"], trust: LocalUnknown);

        var verdict = AdministratorViabilityGuard.Evaluate(pre, post);

        verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
        verdict.Detail.Should().Contain("LocalAuth:Enabled");
    }

    [Fact]
    public void An_unknown_entra_reading_fails_closed_for_the_viable_administrator_invariant()
    {
        var verdict = AdministratorViabilityGuard.Evaluate(Same(["a"], ["a"], trust: EntraUnknown), Same(["a"], ["a"], trust: EntraUnknown));

        verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void Trust_in_the_post_state_matters_as_much_as_trust_in_the_pre_state()
    {
        var verdict = AdministratorViabilityGuard.Evaluate(Same(["a"], ["a"]), Same(["a"], ["a"], trust: LocalUnknown));

        verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    // ----- C-13-R: strict revocation -----

    [Fact]
    public void Revocation_is_refused_when_the_only_entra_independent_administrator_is_the_revoked_account_itself()
    {
        // After the designation is cleared the account is an ordinary administrator and would count as the fallback.
        var post = Same(["x"], ["x"]);

        var verdict = AdministratorViabilityGuard.EvaluateRevocation(post, "x");

        verdict.Kind.Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired);
        verdict.Detail.Should().Contain("another ordinary Platform Administrator");
    }

    [Fact]
    public void Revocation_is_allowed_when_another_entra_independent_administrator_exists()
    {
        AdministratorViabilityGuard.EvaluateRevocation(Same(["x", "y"], ["x", "y"]), "x").IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Revocation_does_not_accept_an_entra_only_administrator_as_the_fallback()
    {
        AdministratorViabilityGuard.EvaluateRevocation(Same(["x", "y"], ["x"]), "x").Kind
            .Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired);
    }

    [Fact]
    public void Revocation_is_refused_whatever_the_recovery_posture_was_because_the_rule_is_strict()
    {
        var post = Same(["x"], ["x"], recoveryHealthy: true);

        AdministratorViabilityGuard.EvaluateRevocation(post, "x").IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Revocation_is_judged_in_both_views()
    {
        var post = State(
            Snapshot(ViabilityView.Now, ["x", "y"], ["x", "y"]),
            Snapshot(ViabilityView.Restart, ["x"], ["x"]));

        var verdict = AdministratorViabilityGuard.EvaluateRevocation(post, "x");

        verdict.IsAllowed.Should().BeFalse();
        verdict.View.Should().Be(ViabilityView.Restart);
    }

    [Fact]
    public void Revocation_needs_only_the_local_reading_not_the_entra_one()
    {
        var post = Same(["x", "y"], ["x", "y"], trust: EntraUnknown);

        AdministratorViabilityGuard.EvaluateRevocation(post, "x").IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Revocation_fails_closed_when_the_local_reading_is_not_trusted()
    {
        AdministratorViabilityGuard.EvaluateRevocation(Same(["x", "y"], ["x", "y"], trust: LocalUnknown), "x").Kind
            .Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    // ----- Generated corpus -----

    [Fact]
    public void Over_every_small_state_pair_a_superset_post_state_is_never_refused_and_a_refusal_always_follows_the_shape()
    {
        var ids = new[] { "a", "b" };
        var subsets = Enumerable.Range(0, 1 << ids.Length)
            .Select(mask => ids.Where((_, i) => (mask & (1 << i)) != 0).ToArray())
            .ToList();

        foreach (var preVoa in subsets)
        foreach (var postVoa in subsets)
        foreach (var preHealthy in new[] { false, true })
        foreach (var postHealthy in new[] { false, true })
        {
            var preEi = preVoa.Take(1).ToArray();
            var postEi = postVoa.Take(1).ToArray();
            var pre = Same(preVoa, preEi, preHealthy);
            var post = Same(postVoa, postEi, postHealthy);

            var verdict = AdministratorViabilityGuard.Evaluate(pre, post);

            var viableViolation = preVoa.Length >= 1 && postVoa.Length == 0;
            var fallbackViolation = (preHealthy || preEi.Length >= 1) && !(postHealthy || postEi.Length >= 1);
            (verdict.Kind != ViabilityVerdictKind.Allowed).Should().Be(
                viableViolation || fallbackViolation,
                $"pre voa [{string.Join(',', preVoa)}] healthy {preHealthy}, post voa [{string.Join(',', postVoa)}] healthy {postHealthy}");
        }
    }
}
