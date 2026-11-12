using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The safe-post-state proof of the shared guard (IAM-10.2b WP3 review, M-1). A change whose own result still holds a viable
/// Entra-independent ordinary administrator in both views satisfies I-VIABLE and I-FALLBACK whatever Microsoft sign-in can do, so
/// an Entra reading that cannot be established is no reason to refuse it. Nothing else is exempt: every case below that is not
/// allowed fails closed exactly as before.
/// </summary>
public sealed class AdministratorViabilitySafePostStateTests
{
    private static readonly ConfigurationTrust EntraUnknown = new(
        ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Unknown, ["The client secret could not be read."]);

    private static readonly ConfigurationTrust EntraUntrusted = new(
        ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Untrusted, ["The Entra options failed validation."]);

    private static readonly ConfigurationTrust LocalUnknown = new(
        ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Unknown, ["Set LocalAuth:Enabled."]);

    private static readonly ConfigurationTrust LocalUntrusted = new(
        ConfigurationTrustLevel.Untrusted, ConfigurationTrustLevel.Unknown, ["LocalAuth:Enabled could not be parsed."]);

    private static AdministratorViabilityState Read(
        IReadOnlyList<AdministratorFacts> administrators,
        ConfigurationTrust trust,
        SignInAvailability? now = null,
        SignInAvailability? restart = null,
        RecoveryAccessFacts? recovery = null) =>
        FromFacts(administrators, now, restart, recovery, trust);

    // ----- allowed: the post-state proves both invariants without Entra -----

    [Theory]
    [MemberData(nameof(UnestablishedEntra))]
    public void A_change_that_leaves_a_viable_local_administrator_is_allowed_while_the_entra_reading_is_not_established(ConfigurationTrust trust)
    {
        // Two local administrators, one removed: the first reduction leaves one who needs no Entra.
        var pre = Read([Admin("a", local: true), Admin("b", local: true)], trust);
        var post = Read([Admin("b", local: true)], trust);

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Removing_an_administrator_who_depended_on_entra_is_allowed_while_a_local_administrator_remains()
    {
        var pre = Read([Admin("local", local: true), Admin("entra", entraTenant: true)], EntraUnknown);
        var post = Read([Admin("local", local: true)], EntraUnknown);

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void The_proof_does_not_need_the_pre_state_to_be_trusted_or_viable()
    {
        // The post-state is what is proven: a repair from a state with no Entra-independent administrator is allowed too.
        var pre = Read([Admin("entra", entraTenant: true)], EntraUnknown);
        var post = Read([Admin("entra", entraTenant: true), Admin("local", local: true)], EntraUnknown);

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Recovery_access_health_is_not_needed_for_the_proof_and_not_counted_by_it()
    {
        var pre = Read([Admin("a", local: true), Admin("b", local: true)], EntraUnknown, recovery: Recovery(healthy: false));
        var post = Read([Admin("b", local: true)], EntraUnknown, recovery: Recovery(healthy: false));

        AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed.Should().BeTrue();
    }

    // ----- not allowed: the proof does not hold, so the evaluation fails closed as it always did -----

    [Fact]
    public void A_post_state_with_only_an_entra_dependent_administrator_still_fails_closed()
    {
        var pre = Read([Admin("local", local: true), Admin("entra", entraTenant: true)], EntraUnknown);
        var post = Read([Admin("entra", entraTenant: true)], EntraUnknown);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_post_state_whose_only_administrator_is_recovery_access_still_fails_closed()
    {
        var pre = Read([Admin("local", local: true), Admin("recovery", local: true, recoveryAccess: true)], EntraUnknown, recovery: Recovery(healthy: true));
        var post = Read([Admin("recovery", local: true, recoveryAccess: true)], EntraUnknown, recovery: Recovery(healthy: true));

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_change_that_removes_the_last_local_administrator_still_fails_closed()
    {
        var pre = Read([Admin("a", local: true)], EntraUnknown);
        var post = Read([], EntraUnknown);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Theory]
    [MemberData(nameof(UntrustedLocal))]
    public void An_unestablished_local_configuration_never_passes_the_proof(ConfigurationTrust trust)
    {
        var pre = Read([Admin("a", local: true), Admin("b", local: true)], trust);
        var post = Read([Admin("b", local: true)], trust);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_local_sign_in_that_is_disabled_in_either_view_never_passes_the_proof()
    {
        var admins = new[] { Admin("a", local: true), Admin("b", local: true) };
        var pre = Read(admins, EntraUnknown);

        foreach (var view in new[] { ViabilityView.Now, ViabilityView.Restart })
        {
            var post = Read(
                [admins[1]],
                EntraUnknown,
                now: Availability(ViabilityView.Now, local: view != ViabilityView.Now),
                restart: Availability(ViabilityView.Restart, local: view != ViabilityView.Restart));

            AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable, $"Local sign-in is off in the {view} view");
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void An_administrator_without_a_local_link_and_an_enabled_credential_is_not_a_local_fallback(bool hasLink, bool credentialEnabled)
    {
        var broken = new AdministratorFacts("b", IsRecoveryAccess: false, hasLink, credentialEnabled, ProvenEntraLinkTenantIds: []);
        var pre = Read([Admin("a", local: true), broken], EntraUnknown);
        var post = Read([broken], EntraUnknown);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_state_without_its_stored_facts_cannot_be_proven_and_fails_closed()
    {
        // A snapshot alone claims a fallback; nothing the proof reads backs it.
        var pre = Same(["a", "b"], ["a", "b"], trust: EntraUnknown);
        var post = Same(["b"], ["b"], trust: EntraUnknown);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_state_without_the_recovery_posture_inputs_is_incomplete_and_fails_closed()
    {
        var pre = Read([Admin("a", local: true), Admin("b", local: true)], EntraUnknown);
        var post = Read([Admin("b", local: true)], EntraUnknown) with { Recovery = null };

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_snapshot_that_claims_more_than_the_facts_prove_is_not_believed()
    {
        var pre = Read([Admin("a", local: true), Admin("b", local: true)], EntraUnknown);
        var facts = new[] { Admin("entra", entraTenant: true) };
        var post = Read(facts, EntraUnknown) with
        {
            Now = Snapshot(ViabilityView.Now, ["entra"], ["entra"]),
            Restart = Snapshot(ViabilityView.Restart, ["entra"], ["entra"]),
        };

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_snapshot_that_disagrees_with_the_facts_in_either_view_fails_closed()
    {
        var pre = Read([Admin("a", local: true), Admin("b", local: true)], EntraUnknown);
        var consistent = Read([Admin("b", local: true)], EntraUnknown);

        foreach (var view in new[] { ViabilityView.Now, ViabilityView.Restart })
        {
            var emptied = Snapshot(view, [], []);
            var post = view == ViabilityView.Now ? consistent with { Now = emptied } : consistent with { Restart = emptied };

            AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable, $"the {view} snapshot disagrees");
        }
    }

    [Fact]
    public void A_snapshot_that_overstates_a_view_against_that_views_own_availability_is_not_believed()
    {
        // In the view where Local sign-in is off, nobody is Entra-independent. The snapshot of that view claims somebody is, as the
        // other view's availability would have it: the proof judges each view under its own availability and does not believe it.
        var pre = Read([Admin("a", local: true), Admin("b", local: true)], EntraUnknown);

        foreach (var view in new[] { ViabilityView.Now, ViabilityView.Restart })
        {
            var post = Read(
                [Admin("b", local: true)],
                EntraUnknown,
                now: Availability(ViabilityView.Now, local: view != ViabilityView.Now),
                restart: Availability(ViabilityView.Restart, local: view != ViabilityView.Restart));
            var overstated = Snapshot(view, ["b"], ["b"]);
            post = view == ViabilityView.Now ? post with { Now = overstated } : post with { Restart = overstated };

            AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable, $"Local sign-in is off in the {view} view");
        }
    }

    [Fact]
    public void The_proof_is_made_in_each_view_separately()
    {
        // The Restart availability reads Local as off while the Now availability reads it on.
        var pre = Read([Admin("a", local: true), Admin("b", local: true)], EntraUnknown);
        var post = Read([Admin("b", local: true)], EntraUnknown, restart: Availability(ViabilityView.Restart, local: false));

        var verdict = AdministratorViabilityGuard.Evaluate(pre, post);

        verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
    }

    [Fact]
    public void A_change_judged_with_a_trusted_entra_reading_is_decided_by_the_ordinary_predicates_not_the_proof()
    {
        // Entra known and the last viable administrator removed: the verdict is the ordinary refusal, never the shortcut.
        var pre = Read([Admin("a", local: true)], ConfigurationTrust.Full);
        var post = Read([], ConfigurationTrust.Full);

        AdministratorViabilityGuard.Evaluate(pre, post).Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator);
    }

    public static TheoryData<ConfigurationTrust> UnestablishedEntra => new() { EntraUnknown, EntraUntrusted };

    public static TheoryData<ConfigurationTrust> UntrustedLocal => new() { LocalUnknown, LocalUntrusted };
}
