using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.AdministratorViability;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// IAM-10.2b WP4B-C: how the native MFA service calls the coordinator, the guard and the candidate store, over scripted states and in-memory
/// stores. The replacement: staged and verified with no gate and no change to the active authenticator, then a switch under the gate judged
/// on the posture alone (I-R3-1), every candidate outcome given its typed conflict, a consumed candidate verified before it is reported. The
/// bare reset as a reducer, the gated raisers, the typed conflict for a concurrency failure, and a recovery code consumed once. The same
/// properties against real SQL Server are in the integration suite.
/// </summary>
public sealed partial class NativeMfaServiceTests
{
    private static readonly ConfigurationTrust AttestedTrust =
        new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], ConfigurationEvidence.Attested);

    private static readonly ConfigurationTrust NoEvidence =
        new(ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Unknown, ["No Management Host has recorded its configuration."], ConfigurationEvidence.Unavailable);

    /// <summary>The harness user designated for Recovery Access, with the posture given; one ordinary administrator besides it unless said.</summary>
    private static AdministratorViabilityState RecoveryState(
        bool enrolled,
        int codes,
        bool otherAdministrator = true,
        bool otherProvenByEntra = false,
        ConfigurationTrust? trust = null)
    {
        var admins = new List<AdministratorFacts> { ViabilityTestData.Admin("usr-e", local: true, recoveryAccess: true) };
        if (otherAdministrator)
            admins.Add(ViabilityTestData.Admin("other", local: !otherProvenByEntra, entraTenant: otherProvenByEntra));
        return ViabilityTestData.FromFacts(
            admins,
            recovery: new RecoveryAccessFacts("usr-e", true, true, true, enrolled, codes),
            trust: trust ?? AttestedTrust);
    }

    /// <summary>An enrolled harness with a staged and verified candidate; returns the candidate's plaintext secret and the version to switch.</summary>
    private static async Task<(Harness Harness, NativeMfaReplacementChallenge Challenge, int Version)> VerifiedReplacementAsync()
    {
        var harness = await Harness.EnrolledAsync();
        var challenge = await harness.Mfa.BeginReplacementAsync(harness.UserId, "emergency");
        var code = Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), harness.Clock.GetUtcNow().ToUnixTimeSeconds());
        var verified = await harness.Mfa.VerifyReplacementAsync(harness.UserId, challenge.CandidateId, challenge.Version, code);
        return (harness, challenge, verified.Version);
    }

    // ------------------------------------------------------------------------------------------------ staging and verification

    [Fact]
    public async Task A_replacement_is_staged_and_verified_without_the_gate_and_without_changing_the_active_authenticator()
    {
        var harness = await Harness.EnrolledAsync();
        var active = harness.Totp.Stored;
        var codes = harness.Recovery.Codes.ToList();

        var challenge = await harness.Mfa.BeginReplacementAsync(harness.UserId, "emergency");
        var candidateSecret = Base32.Decode(challenge.ManualEntryKey);
        candidateSecret.Should().NotEqual(harness.PlaintextSecret, "the candidate is a new secret");
        var verified = await harness.Mfa.VerifyReplacementAsync(
            harness.UserId, challenge.CandidateId, challenge.Version, Rfc6238Totp.Compute(candidateSecret, harness.Clock.GetUtcNow().ToUnixTimeSeconds()));

        verified.Version.Should().Be(challenge.Version + 1);
        harness.Gate.Acquired.Should().Be(1, "only the enrolment took the gate: staging and verifying hold nothing across the operator's input");
        harness.Recorder.Operations.Should().BeEmpty("staging and verifying are not audited changes of the authenticator");
        harness.Totp.Stored.Should().Be(active, "the active authenticator stays usable until the switch");
        harness.Recovery.Codes.Should().Equal(codes);
    }

    [Fact]
    public async Task Possession_is_proven_against_the_candidates_secret_and_never_against_the_active_one()
    {
        var harness = await Harness.EnrolledAsync();
        var challenge = await harness.Mfa.BeginReplacementAsync(harness.UserId, "emergency");
        var unix = harness.Clock.GetUtcNow().ToUnixTimeSeconds();

        var withActiveCode = () => harness.Mfa.VerifyReplacementAsync(
            harness.UserId, challenge.CandidateId, challenge.Version, Rfc6238Totp.Compute(harness.PlaintextSecret!, unix)).AsTask();

        (await withActiveCode.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("not valid");
        harness.Candidates.Candidate!.Status.Should().Be(NativeMfaCandidateStatus.Staged, "a code of the active authenticator proves nothing about the candidate");

        await harness.Mfa.VerifyReplacementAsync(harness.UserId, challenge.CandidateId, challenge.Version, Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), unix));
        harness.Candidates.Candidate!.Status.Should().Be(NativeMfaCandidateStatus.Verified);
        harness.Candidates.Candidate!.MatchedStep.Should().Be(unix / Rfc6238Totp.DefaultPeriodSeconds, "the step its code matched is recorded for the switch");
    }

    [Fact]
    public async Task A_candidate_code_at_or_below_the_active_authenticators_last_used_step_is_not_accepted()
    {
        var harness = await Harness.EnrolledAsync();
        var challenge = await harness.Mfa.BeginReplacementAsync(harness.UserId, "emergency");
        var unix = harness.Clock.GetUtcNow().ToUnixTimeSeconds();
        harness.Totp.Replace(c => c with { LastUsedStep = unix / Rfc6238Totp.DefaultPeriodSeconds + 1 });

        var act = () => harness.Mfa.VerifyReplacementAsync(
            harness.UserId, challenge.CandidateId, challenge.Version, Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), unix)).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>("the step the switch records must never move backwards");
        harness.Candidates.Candidate!.Status.Should().Be(NativeMfaCandidateStatus.Staged);
    }

    [Fact]
    public async Task An_expired_candidate_at_verification_is_discarded_and_reported_as_expired()
    {
        var harness = await Harness.EnrolledAsync();
        var challenge = await harness.Mfa.BeginReplacementAsync(harness.UserId, "emergency");
        harness.Candidates.Candidate = harness.Candidates.Candidate! with { IsExpired = true };

        var act = () => harness.Mfa.VerifyReplacementAsync(
            harness.UserId, challenge.CandidateId, challenge.Version, Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), harness.Clock.GetUtcNow().ToUnixTimeSeconds())).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.AuthenticatorReplacementExpired);
        harness.Candidates.Discards.Should().Be(1, "an expired candidate's protected secret is not left at rest");
    }

    [Fact]
    public async Task Staging_without_an_enrolled_authenticator_is_refused_and_never_falls_back_to_a_reset()
    {
        var harness = Harness.Create();

        var act = () => harness.Mfa.BeginReplacementAsync(harness.UserId, "emergency").AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.AuthenticatorNotEnrolled);
        harness.Recorder.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task Abandoning_a_replacement_discards_its_candidate_and_leaves_the_active_authenticator()
    {
        var harness = await Harness.EnrolledAsync();
        await harness.Mfa.BeginReplacementAsync(harness.UserId, "emergency");
        var active = harness.Totp.Stored;

        (await harness.Mfa.AbandonReplacementAsync(harness.UserId)).Should().BeTrue();

        harness.Candidates.Candidate.Should().BeNull();
        harness.Totp.Stored.Should().Be(active);
    }

    // ------------------------------------------------------------------------------------------------ the switch

    [Fact]
    public async Task A_verified_candidate_switches_under_the_gate_judged_on_the_posture_alone_and_records_the_replacement()
    {
        var (harness, challenge, version) = await VerifiedReplacementAsync();
        harness.UseViability(RecoveryState(enrolled: true, codes: 3), RecoveryState(enrolled: true, codes: 10));
        var gateBefore = harness.Gate.Acquired;

        var result = await harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli);

        result.AlreadyApplied.Should().BeFalse();
        result.RecoveryCodes.Should().HaveCount(10);
        harness.Gate.Acquired.Should().Be(gateBefore + 1);
        harness.Reads.Reads.Should().Be(2, "the state before, then the state the switch left, decided last");
        harness.Totp.Stored!.CredentialId.Should().Be(challenge.CandidateId, "the candidate is the active authenticator now");
        var operation = harness.Recorder.Operations.Should().ContainSingle().Which;
        operation.Operation.Actor.Should().Be(AuditActor.EmergencyCli);
        operation.Staged.Select(e => e.Action).Should().Equal(AuditActionNames.MfaReplaced, AuditActionNames.RecoveryCodesGenerated);
        harness.Recorder.Single(AuditActionNames.MfaReplaced).Fields[ConfigurationEvidenceNames.AuditDetailName]
            .Should().Be(ConfigurationEvidenceNames.NotRequired, "a replacement is decided on the posture alone");
        harness.Recorder.Single(AuditActionNames.RecoveryCodesGenerated).Fields["regenerated"].Should().Be(true);
        harness.RecordedText().Should().NotContain(result.RecoveryCodes).And.NotContain(challenge.CandidateId, "a candidate id is never recorded");
        harness.Candidates.SwitchRequests.Should().ContainSingle().Which.RecoveryCodes.Should().OnlyContain(c => c.UserId.Value == "usr-e" && c.ConsumedAtUtc == null);
    }

    [Fact]
    public async Task A_switch_whose_decision_was_not_made_on_the_posture_alone_is_rolled_back_as_administrator_access_changed_whatever_its_verdict()
    {
        // I-R3-1. Another administrator's facts moved between the state before and the state after (an Entra link proven at sign-in, which
        // takes no gate). The guard goes to the full evaluation, which allows (an administrator signs in without Microsoft); the replacement's
        // own rule was not applied, so the switch is not kept.
        var (harness, challenge, version) = await VerifiedReplacementAsync();
        harness.RollingBack();
        var active = harness.Totp.Stored;
        harness.UseViability(
            RecoveryState(enrolled: true, codes: 3),
            ViabilityTestData.FromFacts(
                [ViabilityTestData.Admin("usr-e", local: true, recoveryAccess: true), ViabilityTestData.Admin("other", local: true, entraTenant: true)],
                recovery: new RecoveryAccessFacts("usr-e", true, true, true, true, 10),
                trust: AttestedTrust));

        var act = () => harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.AdministratorAccessChanged);
        harness.Totp.Stored.Should().Be(active, "the switch was rolled back: the active authenticator stays in place");
        harness.Candidates.Candidate!.Status.Should().Be(NativeMfaCandidateStatus.Verified, "and the candidate is verified again, at its version");
        harness.Recorder.Actions.Should().BeEmpty();
        var refusal = harness.Events.Single(AuditActionNames.AdministratorViabilityBlocked);
        refusal.Target!.Id.Should().Be("usr-e");
        refusal.Fields["operation"].Should().Be("mfa.replace");
        refusal.Fields["code"].Should().Be(GovernanceConflictCodes.AdministratorAccessChanged);
        refusal.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested, "the refusal names the evidence class of the decision it was made on");
    }

    [Fact]
    public async Task A_switch_whose_fall_through_decision_is_unavailable_is_still_reported_as_a_change_of_access_and_not_as_missing_configuration()
    {
        var (harness, challenge, version) = await VerifiedReplacementAsync();
        harness.RollingBack();
        harness.UseViability(
            RecoveryState(enrolled: true, codes: 3, trust: NoEvidence),
            ViabilityTestData.FromFacts(
                [ViabilityTestData.Admin("usr-e", local: true, recoveryAccess: true), ViabilityTestData.Admin("other", local: false, entraTenant: true)],
                recovery: new RecoveryAccessFacts("usr-e", true, true, true, true, 10),
                trust: NoEvidence));

        var act = () => harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.AdministratorAccessChanged);
        harness.Events.Single(AuditActionNames.AdministratorViabilityBlocked).Fields[ConfigurationEvidenceNames.AuditDetailName]
            .Should().Be(ConfigurationEvidenceNames.Unavailable, "the row still says which evidence the discarded decision was made under");
    }

    [Fact]
    public async Task A_switch_that_would_leave_the_recovery_posture_worse_is_refused_on_the_posture_and_recorded()
    {
        var (harness, challenge, version) = await VerifiedReplacementAsync();
        harness.RollingBack();
        var active = harness.Totp.Stored;
        harness.UseViability(RecoveryState(enrolled: true, codes: 3), RecoveryState(enrolled: true, codes: 0));

        var act = () => harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.RecoveryAccessPostureReduced);
        harness.Totp.Stored.Should().Be(active);
        var refusal = harness.Events.Single(AuditActionNames.AdministratorViabilityBlocked);
        refusal.Fields["operation"].Should().Be("mfa.replace");
        refusal.Fields["code"].Should().Be(GovernanceConflictCodes.RecoveryAccessPostureReduced);
        refusal.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.NotRequired);
    }

    [Fact]
    public async Task A_consumed_candidate_whose_switch_is_the_active_authenticator_is_reported_as_applied_earlier_without_codes()
    {
        // S-1: a request whose switch committed but whose result was lost is repeated. The candidate is consumed, and the active
        // authenticator is the one its switch made: the replacement is in effect, and its codes cannot be shown again.
        var (harness, challenge, version) = await VerifiedReplacementAsync();
        harness.UseViability(RecoveryState(enrolled: true, codes: 3), RecoveryState(enrolled: true, codes: 10));
        await harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli);
        harness.Recorder.Operations.Clear();

        var again = await harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli);

        again.AlreadyApplied.Should().BeTrue();
        again.RecoveryCodes.Should().BeEmpty("the codes of the earlier request were not received and cannot be shown again");
        harness.Recorder.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue("nothing changed and nothing is recorded");
        harness.Events.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task A_consumed_candidate_whose_switch_is_no_longer_the_active_authenticator_is_not_reported_as_applied()
    {
        var (harness, challenge, version) = await VerifiedReplacementAsync();
        harness.UseViability(RecoveryState(enrolled: true, codes: 3), RecoveryState(enrolled: true, codes: 10));
        await harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli);
        harness.Totp.Replace(c => c with { CredentialId = "replaced-again" });

        var act = () => harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid,
            "a consumed candidate alone is not proof that this replacement is the one in effect");
    }

    public static IEnumerable<object[]> RefusedSwitches() =>
    [
        [NativeMfaCandidateOutcome.NoActiveEnrollment, GovernanceConflictCodes.AuthenticatorNotEnrolled],
        [NativeMfaCandidateOutcome.NotFound, GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid],
        [NativeMfaCandidateOutcome.Superseded, GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid],
        [NativeMfaCandidateOutcome.VersionMismatch, GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid],
        [NativeMfaCandidateOutcome.AlreadyVerified, GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid],
        [NativeMfaCandidateOutcome.ActiveEnrollmentChanged, GovernanceConflictCodes.AuthenticatorChanged],
        [NativeMfaCandidateOutcome.Expired, GovernanceConflictCodes.AuthenticatorReplacementExpired],
    ];

    [Theory]
    [MemberData(nameof(RefusedSwitches))]
    public async Task A_switch_the_store_refused_is_a_typed_conflict_that_changed_nothing_and_recorded_nothing(NativeMfaCandidateOutcome outcome, string code)
    {
        var (harness, challenge, version) = await VerifiedReplacementAsync();
        var active = harness.Totp.Stored;
        var codes = harness.Recovery.Codes.ToList();
        harness.Candidates.NextSwitch = outcome;

        var act = () => harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(code);
        harness.Totp.Stored.Should().Be(active);
        harness.Recovery.Codes.Should().Equal(codes);
        harness.Recorder.Actions.Should().BeEmpty();
        harness.Events.Written.Should().BeEmpty("a refused switch is not a viability refusal");
        harness.Candidates.Discards.Should().Be(outcome == NativeMfaCandidateOutcome.Expired ? 1 : 0, "only an expired candidate is removed, after the unit");
    }

    [Fact]
    public async Task A_switch_of_a_candidate_that_was_never_verified_is_a_defect_not_a_business_outcome()
    {
        var (harness, challenge, version) = await VerifiedReplacementAsync();
        harness.Candidates.NextSwitch = NativeMfaCandidateOutcome.NotVerified;

        var act = () => harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli).AsTask();

        await act.Should().ThrowAsync<AuditInvariantException>();
        harness.Recorder.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_switch_that_fails_part_way_leaves_nothing_committed_and_is_not_caught()
    {
        // S-2: no savepoint protects the unit from a half-applied switch, so a failure inside it must end the unit.
        var (harness, challenge, version) = await VerifiedReplacementAsync();
        harness.RollingBack();
        var active = harness.Totp.Stored;
        harness.Candidates.FailAfterApplying = true;

        var act = () => harness.Mfa.CompleteReplacementAsync(harness.UserId, challenge.CandidateId, version, AuditActor.EmergencyCli).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected*");
        harness.Totp.Stored.Should().Be(active, "the unit rolled the partial switch back");
        harness.Recorder.Actions.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------------------------------ the bare reset

    [Fact]
    public async Task A_bare_reset_that_takes_a_healthy_posture_away_with_no_entra_independent_administrator_is_refused_and_recorded()
    {
        var harness = (await Harness.EnrolledAsync()).RollingBack();
        var active = harness.Totp.Stored;
        harness.UseViability(
            RecoveryState(enrolled: true, codes: 3, otherProvenByEntra: true),
            RecoveryState(enrolled: false, codes: 0, otherProvenByEntra: true));

        var act = () => harness.Mfa.ResetAsync(harness.UserId, AuditActor.EmergencyCli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        harness.Totp.Stored.Should().Be(active, "the refused reset was rolled back");
        harness.Recorder.Actions.Should().BeEmpty();
        var refusal = harness.Events.Single(AuditActionNames.AdministratorViabilityBlocked);
        refusal.Fields["operation"].Should().Be("mfa.reset");
        refusal.Fields["code"].Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        refusal.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested);
    }

    [Fact]
    public async Task A_bare_reset_that_takes_a_healthy_posture_away_with_an_entra_independent_administrator_stands_with_attested_evidence()
    {
        var harness = await Harness.EnrolledAsync();
        harness.UseViability(RecoveryState(enrolled: true, codes: 3), RecoveryState(enrolled: false, codes: 0));

        await harness.Mfa.ResetAsync(harness.UserId, AuditActor.EmergencyCli);

        harness.Totp.Stored.Should().BeNull();
        harness.Recorder.Single(AuditActionNames.MfaReset).Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested);
    }

    [Fact]
    public async Task A_bare_reset_of_a_posture_that_was_not_healthy_needs_no_evidence_and_records_not_required()
    {
        var harness = await Harness.EnrolledAsync();
        harness.UseViability(RecoveryState(enrolled: true, codes: 0, trust: NoEvidence), RecoveryState(enrolled: false, codes: 0, trust: NoEvidence));

        await harness.Mfa.ResetAsync(harness.UserId, AuditActor.EmergencyCli);

        harness.Recorder.Single(AuditActionNames.MfaReset).Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.NotRequired);
    }

    [Fact]
    public async Task A_bare_reset_that_would_reduce_without_an_established_local_reading_is_refused_as_unavailable()
    {
        var harness = (await Harness.EnrolledAsync()).RollingBack();
        harness.UseViability(RecoveryState(enrolled: true, codes: 3, trust: NoEvidence), RecoveryState(enrolled: false, codes: 0, trust: NoEvidence));

        var act = () => harness.Mfa.ResetAsync(harness.UserId, AuditActor.EmergencyCli).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be(GovernanceConflictCodes.AdministratorViabilityUnavailable);
        harness.Events.Single(AuditActionNames.AdministratorViabilityBlocked).Fields[ConfigurationEvidenceNames.AuditDetailName]
            .Should().Be(ConfigurationEvidenceNames.Unavailable);
    }

    [Fact]
    public async Task A_reset_that_meets_a_concurrent_change_of_the_authenticator_is_a_typed_retryable_conflict_that_keeps_its_cause()
    {
        var harness = await Harness.EnrolledAsync();
        harness.Totp.ConcurrencyFailureOnNextDelete = true;

        var act = () => harness.Mfa.ResetAsync(harness.UserId, AuditActor.EmergencyCli).AsTask();

        var conflict = (await act.Should().ThrowAsync<GovernanceConflictException>()).Which;
        conflict.Code.Should().Be(GovernanceConflictCodes.AuthenticatorChanged);
        conflict.InnerException.Should().BeOfType<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>();
        harness.Recorder.Actions.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------------------------------ the gated raisers and the sign-in

    [Fact]
    public async Task A_first_enrolment_and_a_regeneration_of_the_codes_take_the_gate_and_assert_they_reduced_nothing()
    {
        var harness = await Harness.EnrolledAsync();
        harness.Gate.Acquired.Should().Be(1, "the enrolment took the gate");
        var readsAfterEnrolment = harness.Reads.Reads;
        readsAfterEnrolment.Should().Be(2, "the state before the enrolment and after it, for the raise-only assertion");

        await harness.Mfa.RegenerateRecoveryCodesAsync(harness.UserId, AuditActor.EmergencyCli);

        harness.Gate.Acquired.Should().Be(2);
        harness.Reads.Reads.Should().Be(readsAfterEnrolment + 2);
    }

    // ------------------------------------------------------------------------------------------------ the candidate store

    /// <summary>One user's candidate over the harness's authenticator and codes, with the store's conditions; a case can script a switch.</summary>
    private sealed class MemoryCandidates(Harness harness) : INativeMfaReplacementCandidateRepository
    {
        public NativeMfaReplacementCandidate? Candidate { get; set; }

        public List<NativeMfaSwitchRequest> SwitchRequests { get; } = [];

        public int Discards { get; private set; }

        /// <summary>An outcome the next switch reports, writing nothing, as the store does for a refused switch.</summary>
        public NativeMfaCandidateOutcome? NextSwitch { get; set; }

        /// <summary>Plays a statement that fails after the switch's first writes (the codes' inserts), inside the caller's unit.</summary>
        public bool FailAfterApplying { get; set; }

        public ValueTask<NativeMfaCandidateStageResult> StageAsync(UserId userId, byte[] protectedSecret, string protectorKeyId, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            if (harness.Totp.Stored is not { IsEnrolled: true } active)
                return ValueTask.FromResult(new NativeMfaCandidateStageResult(NativeMfaCandidateOutcome.NoActiveEnrollment, null));
            var now = harness.Clock.GetUtcNow();
            Candidate = new NativeMfaReplacementCandidate(
                Guid.NewGuid().ToString("N"), userId, protectedSecret, protectorKeyId, active.CredentialId, NativeMfaCandidateStatus.Staged, 1, now, now + lifetime, null, null, false);
            return ValueTask.FromResult(new NativeMfaCandidateStageResult(NativeMfaCandidateOutcome.Applied, Candidate));
        }

        public ValueTask<NativeMfaReplacementCandidate?> GetAsync(UserId userId, string candidateId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Candidate is { } c && c.CandidateId == candidateId && c.UserId == userId ? c : null);

        public ValueTask<NativeMfaCandidateVerifyResult> MarkVerifiedAsync(UserId userId, string candidateId, int expectedVersion, long matchedStep, CancellationToken cancellationToken = default)
        {
            if (Candidate is not { Status: NativeMfaCandidateStatus.Staged } c || c.CandidateId != candidateId || c.Version != expectedVersion || c.IsExpired)
                return ValueTask.FromResult(new NativeMfaCandidateVerifyResult(NativeMfaCandidateOutcome.NotFound, null));
            Candidate = c with { Status = NativeMfaCandidateStatus.Verified, Version = c.Version + 1, MatchedStep = matchedStep, VerifiedAtUtc = harness.Clock.GetUtcNow() };
            return ValueTask.FromResult(new NativeMfaCandidateVerifyResult(NativeMfaCandidateOutcome.Applied, Candidate.Version));
        }

        public async ValueTask<NativeMfaSwitchResult> SwitchAsync(NativeMfaSwitchRequest request, CancellationToken cancellationToken = default)
        {
            SwitchRequests.Add(request);
            if (NextSwitch is { } scripted)
            {
                NextSwitch = null;
                return new NativeMfaSwitchResult(scripted, null);
            }

            if (Candidate is not { } c || c.CandidateId != request.CandidateId)
                return new NativeMfaSwitchResult(NativeMfaCandidateOutcome.NotFound, null);
            if (c.Status == NativeMfaCandidateStatus.Consumed)
                return new NativeMfaSwitchResult(NativeMfaCandidateOutcome.Consumed, null);
            if (c.Status != NativeMfaCandidateStatus.Verified || c.Version != request.ExpectedVersion)
                return new NativeMfaSwitchResult(NativeMfaCandidateOutcome.VersionMismatch, null);

            harness.Totp.Replace(t => t with { ProtectedSecret = c.ProtectedSecret, CredentialId = c.CandidateId, LastUsedStep = c.MatchedStep });
            Candidate = c with { Status = NativeMfaCandidateStatus.Consumed, Version = c.Version + 1, ProtectedSecret = [] };
            await harness.Recovery.ReplaceAllAsync(request.UserId, request.RecoveryCodes, cancellationToken);
            if (FailAfterApplying)
            {
                FailAfterApplying = false;
                throw new InvalidOperationException("Injected: a statement of the switch failed after its first writes.");
            }

            return new NativeMfaSwitchResult(NativeMfaCandidateOutcome.Applied, c.CandidateId);
        }

        public ValueTask<int> DiscardAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            Discards++;
            var removed = Candidate is null ? 0 : 1;
            Candidate = null;
            return ValueTask.FromResult(removed);
        }
    }
}
