using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// IAM-10.2b WP4B-C: the decisions the reducers of Recovery Access and of the authenticator will use. A bare reset and an authenticator
/// replacement are judged on what they can do to the posture, and on nothing they cannot affect; a designation and a strict revocation
/// keep the rules they had. Each says the class of evidence it used. Every case is pure: the states are built the way the service builds
/// them, and the SQL proof is in <c>AdministratorViabilityAttemptSqlServerTests</c>.
/// </summary>
public sealed class AdministratorViabilityPostureDecisionTests
{
    private static readonly RecoveryAccessFacts Healthy = new("recovery", true, true, true, true, 3);
    private static readonly RecoveryAccessFacts Reset = Healthy with { NativeMfaEnrolled = false, UnusedRecoveryCodes = 0 };

    /// <summary>Not healthy for a reason an authenticator has nothing to do with: the account's Local credential is disabled.</summary>
    private static readonly RecoveryAccessFacts UnhealthyWithAuthenticator = Healthy with { LocalCredentialEnabled = false };

    private static readonly AdministratorFacts RecoveryAdmin = Admin("recovery", local: true, recoveryAccess: true);
    private static readonly AdministratorFacts LocalAdmin = Admin("a", local: true);
    private static readonly AdministratorFacts EntraAdmin = Admin("b", entraTenant: true);

    /// <summary>What a startup attestation gives: Local established, Microsoft sign-in not.</summary>
    private static ConfigurationTrust StartupAttestation() =>
        new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Unknown, ["not built yet"], ConfigurationEvidence.Attested);

    /// <summary>A fresh attestation with both readings established.</summary>
    private static ConfigurationTrust FullAttestation() =>
        new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], ConfigurationEvidence.Attested);

    /// <summary>What unusable evidence gives: nothing is trusted.</summary>
    private static ConfigurationTrust NoEvidence() =>
        new(ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Unknown, ["no attestation"], ConfigurationEvidence.Unavailable);

    private static AdministratorViabilityState Over(
        IReadOnlyList<AdministratorFacts> administrators,
        RecoveryAccessFacts recovery,
        ConfigurationTrust trust,
        SignInAvailability? now = null,
        SignInAvailability? restart = null) =>
        FromFacts(administrators, now, restart, recovery, trust);

    private static ViabilityDecision BareReset(
        IReadOnlyList<AdministratorFacts> administrators,
        ConfigurationTrust trust,
        RecoveryAccessFacts? before = null,
        RecoveryAccessFacts? after = null,
        SignInAvailability? now = null,
        SignInAvailability? restart = null,
        string user = "recovery") =>
        AdministratorViabilityGuard.DecideBareReset(
            Over(administrators, before ?? Healthy, trust, now, restart),
            Over(administrators, after ?? Reset, trust, now, restart),
            user);

    // ---------------------------------------------------------------------------- bare reset: cannot reduce

    public static IEnumerable<object[]> EveryTrust() => new[]
    {
        new object[] { "no evidence" },
        new object[] { "startup attestation" },
        new object[] { "full attestation" },
    };

    private static ConfigurationTrust TrustOf(string name) => name switch
    {
        "no evidence" => NoEvidence(),
        "startup attestation" => StartupAttestation(),
        _ => FullAttestation(),
    };

    [Theory]
    [MemberData(nameof(EveryTrust))]
    public void A_reset_of_an_account_that_is_not_the_recovery_account_changes_no_input_and_needs_no_evidence(string trust)
    {
        var decision = BareReset([LocalAdmin, RecoveryAdmin], TrustOf(trust), after: Healthy, user: "a");

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
        decision.Basis.Should().Be(ViabilityDecisionBasis.PostureOnly);
    }

    [Theory]
    [MemberData(nameof(EveryTrust))]
    public void A_reset_when_the_posture_is_already_not_healthy_cannot_reduce_and_needs_no_evidence(string trust)
    {
        // Nobody can sign in and nobody is Entra-independent: the invariants are already violated, and that is no reason to refuse.
        var decision = BareReset(
            [RecoveryAdmin],
            TrustOf(trust),
            before: UnhealthyWithAuthenticator,
            after: UnhealthyWithAuthenticator with { NativeMfaEnrolled = false, UnusedRecoveryCodes = 0 });

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
    }

    [Fact]
    public void A_reset_that_changed_nothing_in_the_posture_needs_no_evidence() =>
        BareReset([RecoveryAdmin], NoEvidence(), after: Healthy).Evidence.Should().Be(ConfigurationEvidence.NotRequired);

    [Fact]
    public void A_recovery_code_consumed_at_a_sign_in_during_the_reset_of_another_account_is_not_this_writers_doing()
    {
        // The last code of the Recovery Access account goes during the reset of somebody else's authenticator: the posture falls, and the
        // writer cannot have caused it. There is no Entra-independent administrator, so blaming the writer would be a false refusal.
        var decision = BareReset(
            [EntraAdmin, RecoveryAdmin],
            NoEvidence(),
            before: Healthy with { UnusedRecoveryCodes = 1 },
            after: Healthy with { UnusedRecoveryCodes = 0 },
            user: "b");

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
    }

    // ---------------------------------------------------------------------------- bare reset: takes the posture away

    public static IEnumerable<object[]> WhateverMicrosoftSignInDoes()
    {
        foreach (var entraTrust in new[] { ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Untrusted })
        {
            foreach (var usable in new[] { true, false })
                yield return new object[] { entraTrust, usable };
        }
    }

    [Theory]
    [MemberData(nameof(WhateverMicrosoftSignInDoes))]
    public void A_reset_that_takes_a_healthy_posture_away_is_allowed_on_a_local_administrator_whatever_microsoft_sign_in_does(ConfigurationTrustLevel entraTrust, bool usable)
    {
        var trust = new ConfigurationTrust(ConfigurationTrustLevel.Trusted, entraTrust, [], ConfigurationEvidence.Attested);
        var decision = BareReset(
            [LocalAdmin, RecoveryAdmin],
            trust,
            now: Availability(ViabilityView.Now, local: true, entra: usable),
            restart: Availability(ViabilityView.Restart, local: true, entra: usable));

        decision.IsAllowed.Should().BeTrue("an enabled Local administrator in both views is the whole of what I-FALLBACK needs");
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested, "it used the attested Local reading");
        decision.Basis.Should().Be(ViabilityDecisionBasis.LocalSignIn);
    }

    [Theory]
    [MemberData(nameof(WhateverMicrosoftSignInDoes))]
    public void A_reset_that_leaves_only_microsoft_administrators_is_refused_whatever_microsoft_sign_in_does(ConfigurationTrustLevel entraTrust, bool usable)
    {
        var trust = new ConfigurationTrust(ConfigurationTrustLevel.Trusted, entraTrust, [], ConfigurationEvidence.Attested);
        var decision = BareReset(
            [EntraAdmin, RecoveryAdmin],
            trust,
            now: Availability(ViabilityView.Now, local: true, entra: usable),
            restart: Availability(ViabilityView.Restart, local: true, entra: usable));

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired, "a Microsoft administrator is not the fallback, and trust in Microsoft sign-in cannot make one");
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        decision.Refusal().Code.Should().Be("entra_independent_fallback_required");
    }

    [Theory]
    [InlineData(true, false, ViabilityView.Restart)]
    [InlineData(false, true, ViabilityView.Now)]
    public void Each_view_is_judged_on_its_own_local_setting(bool localNow, bool localRestart, ViabilityView refused)
    {
        var decision = BareReset(
            [LocalAdmin, RecoveryAdmin],
            StartupAttestation(),
            now: Availability(ViabilityView.Now, local: localNow, entra: false),
            restart: Availability(ViabilityView.Restart, local: localRestart, entra: false));

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired);
        decision.Verdict.View.Should().Be(refused);
    }

    [Fact]
    public void The_recovery_account_is_never_its_own_fallback()
    {
        var decision = BareReset([RecoveryAdmin], StartupAttestation());

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired);
    }

    [Fact]
    public void A_reset_that_takes_the_posture_away_with_no_local_reading_is_refused_as_unavailable()
    {
        var decision = BareReset([LocalAdmin, RecoveryAdmin], NoEvidence());

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
        decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        decision.Basis.Should().Be(ViabilityDecisionBasis.ConfigurationUnavailable);
        decision.Refusal().Code.Should().Be("administrator_viability_unavailable");
    }

    // ---------------------------------------------------------------------------- the predicate cannot be used for anything else

    public static IEnumerable<object[]> ChangesThatAreNotJustAnAuthenticator()
    {
        yield return new object[] { "the role of the account", Healthy with { HoldsAdministratorRole = false } };
        yield return new object[] { "the Local link of the account", Healthy with { HasLocalLink = false } };
        yield return new object[] { "the Local credential of the account", Healthy with { LocalCredentialEnabled = false } };
        yield return new object[] { "who is designated", Healthy with { DesignatedUserId = "other" } };
    }

    [Theory]
    [MemberData(nameof(ChangesThatAreNotJustAnAuthenticator))]
    public void A_posture_change_that_is_not_an_authenticator_goes_to_the_full_evaluation_not_the_predicate(string change, RecoveryAccessFacts after)
    {
        // There is no Local administrator and Microsoft sign-in is not established, so the full evaluation fails closed. Either predicate
        // would have allowed this.
        var administrators = new[] { EntraAdmin, RecoveryAdmin };
        var pre = Over(administrators, UnhealthyWithAuthenticator with { LocalCredentialEnabled = true, UnusedRecoveryCodes = 0 }, StartupAttestation());
        var post = Over(administrators, after with { UnusedRecoveryCodes = 0 }, StartupAttestation());

        var reset = AdministratorViabilityGuard.DecideBareReset(pre, post, "recovery");
        var replacement = AdministratorViabilityGuard.DecideReplacement(pre, post, "recovery");

        reset.Basis.Should().NotBe(ViabilityDecisionBasis.PostureOnly, change);
        replacement.Basis.Should().NotBe(ViabilityDecisionBasis.PostureOnly, change);
        reset.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable, change);
        replacement.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable, change);
    }

    [Fact]
    public void An_administrators_fact_that_moved_takes_the_decision_out_of_the_predicate()
    {
        var pre = Over([LocalAdmin, EntraAdmin, RecoveryAdmin], Healthy, StartupAttestation());
        var post = Over([LocalAdmin with { LocalCredentialEnabled = false }, EntraAdmin, RecoveryAdmin], Healthy, StartupAttestation());

        foreach (var decision in new[]
                 {
                     AdministratorViabilityGuard.DecideBareReset(pre, post, "a"),
                     AdministratorViabilityGuard.DecideReplacement(pre, post, "a"),
                 })
        {
            decision.Basis.Should().Be(ViabilityDecisionBasis.ConfigurationUnavailable);
            decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        }
    }

    [Fact]
    public void A_reset_that_adds_to_the_posture_is_not_a_reset_and_gets_no_posture_proof()
    {
        var pre = Over([EntraAdmin, RecoveryAdmin], Reset, StartupAttestation());
        var post = Over([EntraAdmin, RecoveryAdmin], Healthy, StartupAttestation());

        AdministratorViabilityGuard.DecideBareReset(pre, post, "recovery").Basis.Should().NotBe(ViabilityDecisionBasis.PostureOnly);
    }

    [Fact]
    public void Another_accounts_authenticator_changing_during_the_attempt_gets_no_posture_proof()
    {
        // Only recovery codes going down can happen without the gate. The Recovery Access account's enrolment going away cannot.
        var pre = Over([EntraAdmin, RecoveryAdmin], Healthy, StartupAttestation());
        var post = Over([EntraAdmin, RecoveryAdmin], Healthy with { NativeMfaEnrolled = false }, StartupAttestation());

        AdministratorViabilityGuard.DecideBareReset(pre, post, "b").Basis.Should().NotBe(ViabilityDecisionBasis.PostureOnly);
        AdministratorViabilityGuard.DecideReplacement(pre, post, "b").Basis.Should().NotBe(ViabilityDecisionBasis.PostureOnly);
    }

    [Fact]
    public void A_state_without_its_stored_facts_gets_no_posture_proof()
    {
        var bare = Same(["a"], ["a"], recoveryHealthy: true, trust: NoEvidence());

        AdministratorViabilityGuard.DecideBareReset(bare, bare, "a").Basis.Should().NotBe(ViabilityDecisionBasis.PostureOnly);
        AdministratorViabilityGuard.DecideReplacement(bare, bare, "a").Basis.Should().NotBe(ViabilityDecisionBasis.PostureOnly);
    }

    [Fact]
    public void A_health_flag_that_disagrees_with_the_facts_is_not_believed()
    {
        var pre = Over([EntraAdmin, RecoveryAdmin], Healthy, NoEvidence()) with { RecoveryHealthy = false };
        var post = Over([EntraAdmin, RecoveryAdmin], Reset, NoEvidence());

        AdministratorViabilityGuard.DecideBareReset(pre, post, "recovery").Basis.Should().NotBe(ViabilityDecisionBasis.PostureOnly);
    }

    [Fact]
    public void A_snapshot_that_claims_an_independent_administrator_the_facts_do_not_show_is_not_believed()
    {
        var pre = Over([EntraAdmin, RecoveryAdmin], Healthy, StartupAttestation());
        var honest = Over([EntraAdmin, RecoveryAdmin], Reset, StartupAttestation());
        var claiming = honest with
        {
            Now = Snapshot(ViabilityView.Now, ["b"], ["b"]),
            Restart = Snapshot(ViabilityView.Restart, ["b"], ["b"]),
        };

        var decision = AdministratorViabilityGuard.DecideBareReset(pre, claiming, "recovery");

        decision.IsAllowed.Should().BeFalse();
        decision.Basis.Should().Be(ViabilityDecisionBasis.ConfigurationUnavailable, "the proof does not apply, and the full evaluation has no Microsoft reading to go on");
    }

    // ---------------------------------------------------------------------------- replacement

    [Theory]
    [MemberData(nameof(EveryTrust))]
    public void A_replacement_that_leaves_the_posture_healthy_needs_no_evidence_and_no_local_administrator(string trust)
    {
        var replaced = Healthy with { UnusedRecoveryCodes = 10 };
        var decision = AdministratorViabilityGuard.DecideReplacement(
            Over([RecoveryAdmin], Healthy, TrustOf(trust)),
            Over([RecoveryAdmin], replaced, TrustOf(trust)),
            "recovery");

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
        decision.Basis.Should().Be(ViabilityDecisionBasis.PostureOnly);
    }

    [Fact]
    public void A_replacement_is_allowed_for_a_posture_that_was_not_healthy_and_loses_nothing()
    {
        var before = UnhealthyWithAuthenticator;
        var decision = AdministratorViabilityGuard.DecideReplacement(
            Over([RecoveryAdmin], before, NoEvidence()),
            Over([RecoveryAdmin], before with { UnusedRecoveryCodes = 10 }, NoEvidence()),
            "recovery");

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
    }

    [Fact]
    public void A_replacement_that_makes_a_posture_healthy_is_allowed()
    {
        var decision = AdministratorViabilityGuard.DecideReplacement(
            Over([RecoveryAdmin], Healthy with { UnusedRecoveryCodes = 0 }, NoEvidence()),
            Over([RecoveryAdmin], Healthy, NoEvidence()),
            "recovery");

        decision.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(EveryTrust))]
    public void A_replacement_that_takes_a_healthy_posture_away_is_refused_whatever_the_evidence_says(string trust)
    {
        // A Local administrator exists, so as a reset this would be allowed. A replacement that loses the posture is not a working replacement.
        var decision = AdministratorViabilityGuard.DecideReplacement(
            Over([LocalAdmin, RecoveryAdmin], Healthy, TrustOf(trust)),
            Over([LocalAdmin, RecoveryAdmin], Reset, TrustOf(trust)),
            "recovery");

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.RecoveryPostureReduced);
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired, "the refusal did not read any configuration");
        decision.Refusal().Code.Should().Be("recovery_access_posture_reduced");
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    public void A_replacement_that_loses_what_an_unhealthy_posture_held_is_refused(bool enrolledAfter, int codesAfter)
    {
        var decision = AdministratorViabilityGuard.DecideReplacement(
            Over([RecoveryAdmin], UnhealthyWithAuthenticator, NoEvidence()),
            Over([RecoveryAdmin], UnhealthyWithAuthenticator with { NativeMfaEnrolled = enrolledAfter, UnusedRecoveryCodes = codesAfter }, NoEvidence()),
            "recovery");

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.RecoveryPostureReduced);
    }

    [Fact]
    public void A_replacement_of_an_account_that_is_not_the_recovery_account_touches_no_input()
    {
        var decision = AdministratorViabilityGuard.DecideReplacement(
            Over([LocalAdmin, RecoveryAdmin], Healthy, NoEvidence()),
            Over([LocalAdmin, RecoveryAdmin], Healthy with { UnusedRecoveryCodes = 2 }, NoEvidence()),
            "a");

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
    }

    // ---------------------------------------------------------------------------- designation and strict revocation

    [Fact]
    public void A_designation_that_changed_no_input_needs_no_evidence()
    {
        var state = Over([LocalAdmin, RecoveryAdmin], Healthy, NoEvidence());

        var decision = AdministratorViabilityGuard.DecideDesignation(state, state);

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
        decision.Basis.Should().Be(ViabilityDecisionBasis.InputsUnchanged);
    }

    [Fact]
    public void A_designation_is_refused_as_unavailable_when_the_microsoft_reading_is_missing_and_no_local_administrator_remains()
    {
        var pre = Over([EntraAdmin, LocalAdmin], Recovery(), StartupAttestation());
        var post = Over([EntraAdmin, LocalAdmin with { IsRecoveryAccess = true }], Healthy with { DesignatedUserId = "a" }, StartupAttestation());

        var decision = AdministratorViabilityGuard.DecideDesignation(pre, post);

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
        decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        decision.Mutation.Should().Be(ViabilityMutation.RecoveryAccessDesignation);
    }

    [Fact]
    public void A_designation_proven_safe_on_the_local_reading_alone_records_the_attestation_it_used()
    {
        var second = Admin("c", local: true);
        var pre = Over([second, LocalAdmin], Recovery(), StartupAttestation());
        var post = Over([second, LocalAdmin with { IsRecoveryAccess = true }], Healthy with { DesignatedUserId = "a" }, StartupAttestation());

        var decision = AdministratorViabilityGuard.DecideDesignation(pre, post);

        decision.IsAllowed.Should().BeTrue();
        decision.Basis.Should().Be(ViabilityDecisionBasis.LocalSignIn);
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested, "the proof used the attested Local reading, and an attempt without it is refused");
    }

    [Fact]
    public void A_designation_of_the_last_viable_administrator_is_refused_on_full_evidence_with_the_code_that_names_it()
    {
        var pre = Over([LocalAdmin], Recovery(), FullAttestation());
        var post = Over([LocalAdmin with { IsRecoveryAccess = true }], Healthy with { DesignatedUserId = "a" }, FullAttestation());

        var decision = AdministratorViabilityGuard.DecideDesignation(pre, post);

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator);
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        decision.Basis.Should().Be(ViabilityDecisionBasis.FullEvaluation);
        decision.Refusal().Code.Should().Be("last_viable_administrator");
        decision.Refusal().Message.Should().Contain("Recovery Access");
    }

    [Fact]
    public void A_decision_on_a_hosts_own_reading_records_no_evidence_class()
    {
        var pre = Over([LocalAdmin, EntraAdmin], Recovery(), ConfigurationTrust.Full);
        var post = Over([LocalAdmin with { IsRecoveryAccess = true }, EntraAdmin], Healthy with { DesignatedUserId = "a" }, ConfigurationTrust.Full);

        var decision = AdministratorViabilityGuard.DecideDesignation(pre, post);

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().BeNull("a host reads itself and attests nothing, so the audit detail is left out");
    }

    [Theory]
    [MemberData(nameof(EveryPairOfStates))]
    public void The_decision_of_a_designation_is_the_evaluation_of_the_guard_and_nothing_else(AdministratorViabilityState pre, AdministratorViabilityState post) =>
        AdministratorViabilityGuard.DecideDesignation(pre, post).Verdict.Should().Be(AdministratorViabilityGuard.Evaluate(pre, post));

    public static IEnumerable<object[]> EveryPairOfStates()
    {
        var states = new[]
        {
            Over([LocalAdmin], Recovery(), FullAttestation()),
            Over([LocalAdmin with { IsRecoveryAccess = true }], Healthy with { DesignatedUserId = "a" }, FullAttestation()),
            Over([EntraAdmin], Healthy, StartupAttestation()),
            Over([EntraAdmin, RecoveryAdmin], Reset, NoEvidence()),
            Over([LocalAdmin, EntraAdmin], Healthy, ConfigurationTrust.Full),
        };

        return from pre in states from post in states select new object[] { pre, post };
    }

    [Fact]
    public void A_revocation_with_another_local_administrator_is_allowed_on_the_local_reading_alone()
    {
        var post = Over([LocalAdmin, Admin("recovery", local: true)], Recovery(), StartupAttestation());

        var decision = AdministratorViabilityGuard.DecideRevocation(post, "recovery");

        decision.IsAllowed.Should().BeTrue("a host that has not built its Microsoft options does not stop a decision that does not read them");
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        decision.Basis.Should().Be(ViabilityDecisionBasis.LocalSignIn);
    }

    [Fact]
    public void A_revocation_is_refused_when_only_the_revoked_account_would_be_independent()
    {
        var post = Over([EntraAdmin, Admin("recovery", local: true)], Recovery(), FullAttestation());

        var decision = AdministratorViabilityGuard.DecideRevocation(post, "recovery");

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired, "the account being revoked is not its own fallback");
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        decision.Refusal().Message.Should().Contain("revoke").And.NotContain("Recovery Access is not healthy", "the strict rule does not depend on the posture");
    }

    [Fact]
    public void A_revocation_with_no_local_reading_is_refused_as_unavailable_even_with_a_local_administrator_on_record()
    {
        var post = Over([LocalAdmin, Admin("recovery", local: true)], Recovery(), NoEvidence());

        var decision = AdministratorViabilityGuard.DecideRevocation(post, "recovery");

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
        decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
    }

    // ---------------------------------------------------------------------------- classification and refusal

    public static IEnumerable<object[]> RefusalsOfTheNewMutations() =>
        from mutation in new[]
        {
            ViabilityMutation.RecoveryAccessDesignation,
            ViabilityMutation.RecoveryAccessRevocation,
            ViabilityMutation.AuthenticatorReset,
            ViabilityMutation.AuthenticatorReplacement,
        }
        from kind in new[]
        {
            (ViabilityVerdictKind.LastViableAdministrator, "last_viable_administrator"),
            (ViabilityVerdictKind.EntraIndependentFallbackRequired, "entra_independent_fallback_required"),
            (ViabilityVerdictKind.ConfigurationUnavailable, "administrator_viability_unavailable"),
            (ViabilityVerdictKind.RecoveryPostureReduced, "recovery_access_posture_reduced"),
        }
        select new object[] { mutation, kind.Item1, kind.Item2 };

    [Theory]
    [MemberData(nameof(RefusalsOfTheNewMutations))]
    public void Every_refusal_of_a_new_mutation_has_its_stable_code_and_operator_wording(ViabilityMutation mutation, ViabilityVerdictKind kind, string code)
    {
        var refusal = new ViabilityDecision(
            mutation,
            new ViabilityVerdict(kind, ViabilityView.Now, "detail"),
            ConfigurationEvidence.Attested,
            ViabilityDecisionBasis.FullEvaluation).Refusal();

        refusal.Code.Should().Be(code);
        refusal.Message.Should().NotMatchRegex(@"(?i)\b(C-1\d|I-VIABLE|I-FALLBACK|IAM-\d|WP\d|SEC-\d|OPS-\d)", "an operator reads this, and no planning label belongs in it");
        refusal.Message.Should().NotContain("sign-in method it has", "the wording of the link refusal does not describe these changes");
    }

    [Fact]
    public void An_allowed_decision_has_no_refusal()
    {
        var allowed = new ViabilityDecision(ViabilityMutation.AuthenticatorReset, ViabilityVerdict.Allowed, ConfigurationEvidence.NotRequired, ViabilityDecisionBasis.PostureOnly);

        var refuse = () => allowed.Refusal();

        refuse.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_earlier_writers_keep_their_codes()
    {
        var last = new ViabilityVerdict(ViabilityVerdictKind.LastViableAdministrator, ViabilityView.Now, "");

        ViabilityRefusal.For(last, ViabilityMutation.AdministratorRoleRemoval).Code.Should().Be("last_viable_administrator");
        ViabilityRefusal.For(last, ViabilityMutation.LocalCredentialDisable).Code.Should().Be("last_local_credential");
        ViabilityRefusal.For(last, ViabilityMutation.IdentityLinkRemoval).Code.Should().Be("last_viable_sign_in_method");
        ViabilityRefusal.For(last, ViabilityMutation.RegistrationChange).Code.Should().Be("last_viable_sign_in_method");
    }

    [Theory]
    [InlineData(ConfigurationEvidence.Attested, "attested")]
    [InlineData(ConfigurationEvidence.NotRequired, "not_required")]
    [InlineData(ConfigurationEvidence.Unavailable, "unavailable")]
    public void A_decisions_evidence_is_recorded_in_the_approved_values(ConfigurationEvidence evidence, string value) =>
        evidence.ToAuditValue().Should().Be(value);

    // ---------------------------------------------------------------------------- the scope's contract

    [Fact]
    public async Task The_scope_refuses_every_verification_before_a_capture()
    {
        var scope = await FakeViability.Inert().EnterAsync();

        foreach (var verify in new Func<Task>[]
                 {
                     () => scope.VerifyDesignationAsync().AsTask(),
                     () => scope.VerifyRevocationAsync("a").AsTask(),
                     () => scope.VerifyBareResetAsync("a").AsTask(),
                     () => scope.VerifyReplacementAsync("a").AsTask(),
                 })
        {
            await verify.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task The_scope_requires_the_account_a_verification_is_about()
    {
        var scope = await FakeViability.Inert().EnterAsync();
        await scope.CaptureBeforeAsync();

        foreach (var verify in new Func<Task>[]
                 {
                     () => scope.VerifyRevocationAsync(" ").AsTask(),
                     () => scope.VerifyBareResetAsync(" ").AsTask(),
                     () => scope.VerifyReplacementAsync(" ").AsTask(),
                 })
        {
            await verify.Should().ThrowAsync<ArgumentException>();
        }
    }

    [Fact]
    public async Task The_scope_exposes_the_two_states_of_each_verification()
    {
        var before = Over([LocalAdmin, RecoveryAdmin], Healthy, StartupAttestation());
        var after = Over([LocalAdmin, RecoveryAdmin], Reset, StartupAttestation());
        var scope = await FakeViability.Scripted(new RecordingGate(), before, after).EnterAsync();

        await scope.CaptureBeforeAsync();
        var decision = await scope.VerifyBareResetAsync("recovery");

        scope.Captured.Should().BeSameAs(before);
        scope.After.Should().BeSameAs(after);
        decision.IsAllowed.Should().BeTrue();
    }
}
