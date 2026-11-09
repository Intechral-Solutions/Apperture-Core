using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP4B-C, native MFA writers against real SQL Server on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT, through the production
/// service as the emergency command line composes it: the first enrolment's conditional completion (L-P2), the bare reset as a reducer, and
/// the non-destructive replacement (stage, verify, switch) with every candidate outcome, the I-R3-1 rule, a failed or ambiguous commit, and
/// concurrent Recovery Access and authenticator changes. What a case concludes is read back with SQL of its own. Fails, never skips, without
/// SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class NativeMfaServiceViabilitySqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly ActorId Cli = new(EmergencyAccessService.CliActorId);

    /// <summary>The switch's one deciding statement (the candidate joined and locked): a pause after it holds the applied switch and the gate.</summary>
    private const string SwitchStatement = "INNER JOIN [UserTotpReplacementCandidates] AS c WITH (UPDLOCK, HOLDLOCK)";

    private readonly AuditIsolationCatalogs _catalogs;

    public NativeMfaServiceViabilitySqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ------------------------------------------------------------------------------------------------ arrangement

    /// <summary>The service's clock, moved forward between steps so each code falls in a step of its own (the database's clock decides expiry).</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public long Unix => _now.ToUnixTimeSeconds();

        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    /// <summary>An enrolled account: the secret its authenticator app holds and the recovery codes it was shown.</summary>
    private sealed record Enrolled(ServiceViabilityWorld W, UserId User, byte[] Secret, IReadOnlyList<string> Codes, Clock Clock)
    {
        public string Id => User.Value;
    }

    /// <summary>
    /// A designated Recovery Access account whose authenticator was enrolled through the service (so its posture is healthy and its secret
    /// is known), beside an ordinary administrator who signs in with a Local credential, or only through Microsoft, or none at all.
    /// </summary>
    private async Task<Enrolled> RecoveryAsync(bool rcsi, string fallback = "local", AttestedAuthenticationPayload? attest = null, bool registration = false)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var user = await w.EntraIndependentAdminAsync("recovery");
        await w.World.DesignateRecoveryAccessAsync(user);
        if (fallback == "local")
            await w.EntraIndependentAdminAsync("fallback");
        else if (fallback == "entra")
            await w.EntraOnlyAdminAsync("fallback");
        if (attest is not null)
            await w.AttestAsync(attest, registration);
        return await EnrollAsync(w, new UserId(user));
    }

    private static async Task<Enrolled> EnrollAsync(ServiceViabilityWorld w, UserId user)
    {
        var clock = new Clock();
        await using var db = w.Scratch.NewDb();
        var mfa = w.Mfa(db, time: clock);
        var challenge = await mfa.BeginEnrollmentAsync(user, "account");
        var secret = Base32.Decode(challenge.ManualEntryKey);
        var codes = await mfa.CompleteEnrollmentAsync(user, Rfc6238Totp.Compute(secret, clock.Unix), AuditActor.EmergencyCli);
        return new Enrolled(w, user, secret, codes, clock);
    }

    /// <summary>Stages a candidate and proves possession of it, a step later than anything the account used.</summary>
    private static async Task<(NativeMfaReplacementChallenge Challenge, int Version, byte[] Secret)> StageAndVerifyAsync(Enrolled e, string? spelled = null)
    {
        e.Clock.Advance(60);
        var named = new UserId(spelled ?? e.Id);
        await using var db = e.W.Scratch.NewDb();
        var mfa = e.W.Mfa(db, time: e.Clock);
        var challenge = await mfa.BeginReplacementAsync(named, "account");
        var secret = Base32.Decode(challenge.ManualEntryKey);
        var verified = await mfa.VerifyReplacementAsync(named, challenge.CandidateId, challenge.Version, Rfc6238Totp.Compute(secret, e.Clock.Unix));
        return (challenge, verified.Version, secret);
    }

    private static async Task<NativeMfaReplacementResult> CompleteAsync(Enrolled e, string candidateId, int version, string? spelled = null, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        await using var db = e.W.Scratch.NewDb(interceptors);
        return await e.W.Mfa(db, time: e.Clock).CompleteReplacementAsync(new UserId(spelled ?? e.Id), candidateId, version, AuditActor.EmergencyCli);
    }

    private static async Task<bool> SignInAsync(Enrolled e, byte[] secret, long unix)
    {
        await using var db = e.W.Scratch.NewDb();
        return await e.W.Mfa(db, time: e.Clock).VerifyTotpAsync(e.User, Rfc6238Totp.Compute(secret, unix));
    }

    private static async Task<bool> RecoveryCodeSignInAsync(Enrolled e, string code)
    {
        await using var db = e.W.Scratch.NewDb();
        return await e.W.Mfa(db, time: e.Clock).ConsumeRecoveryCodeAsync(e.User, code);
    }

    private static Task<string> CredentialIdAsync(Enrolled e) =>
        e.W.Scratch.ScalarAsync<string>($"SELECT CredentialId FROM UserTotpCredentials WHERE UserId = N'{e.Id}';");

    /// <summary>The active authenticator and its codes, as a value a failed operation must leave equal.</summary>
    private static Task<string> ActiveAsync(Enrolled e) =>
        e.W.Scratch.ScalarAsync<string>($"""
            SELECT CONCAT(
                (SELECT CONCAT_WS(N'|', CredentialId, CONVERT(nvarchar(64), HASHBYTES('SHA2_256', ProtectedSecret), 2), CONVERT(nvarchar(5), IsEnrolled), CONVERT(nvarchar(40), LastUsedStep))
                   FROM UserTotpCredentials WHERE UserId = N'{e.Id}'),
                N'#',
                (SELECT STRING_AGG(CONCAT_WS(N':', Id, CodeHash, CONVERT(nvarchar(40), ConsumedAtUtc, 127)), N',') WITHIN GROUP (ORDER BY Id)
                   FROM UserMfaRecoveryCodes WHERE UserId = N'{e.Id}'));
            """);

    private static Task<int> CandidateStatusAsync(Enrolled e, string candidateId) =>
        e.W.Scratch.ScalarAsync<int>($"SELECT ISNULL((SELECT Status FROM UserTotpReplacementCandidates WHERE CandidateId = N'{candidateId}'), -1);");

    private static Task<int> CandidateRowsAsync(Enrolled e) =>
        e.W.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpReplacementCandidates WHERE UserId = N'{e.Id}';");

    private static async Task ReachedAsync(PauseAt pause, Task operation)
    {
        var first = await Task.WhenAny(pause.Reached, operation, Task.Delay(Patience));
        if (first == operation)
        {
            await operation;
            throw new Xunit.Sdk.XunitException("The operation finished without reaching the pause.");
        }

        if (first != pause.Reached)
            throw new TimeoutException("The operation did not reach the pause.");
    }

    // ================================================================================================ first enrolment (L-P2)

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_first_enrolment_takes_the_gate_and_completes_only_for_the_pending_secret_whose_code_was_checked(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var user = new UserId(await w.EntraIndependentAdminAsync("user"));
        NativeMfaEnrollmentChallenge first;
        await using (var db = w.Scratch.NewDb())
            first = await w.Mfa(db).BeginEnrollmentAsync(user, "user");
        var code = Rfc6238Totp.Compute(Base32.Decode(first.ManualEntryKey), DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Parked after it read the pending credential (it holds the gate): a begin, which takes no gate, replaces the pending secret.
        var pause = new PauseAt(c => c.CommandText.Contains("FROM [UserTotpCredentials] AS [u]", StringComparison.Ordinal), after: true);
        var complete = Task.Run(async () =>
        {
            await using var db = w.Scratch.NewDb(pause);
            return await w.Mfa(db).CompleteEnrollmentAsync(user, code, AuditActor.EmergencyCli);
        });
        await ReachedAsync(pause, complete);
        (await ProbeGateAsync(w.Scratch)).Should().Be(-1, "the completion holds the gate");
        await using (var db = w.Scratch.NewDb())
            await w.Mfa(db).BeginEnrollmentAsync(user, "user");

        pause.Release();
        var refusal = await Record.ExceptionAsync(() => complete.WaitAsync(Patience));

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AuthenticatorChanged,
            "the code proved the secret that was replaced; no secret nobody verified is enrolled");
        (await w.Scratch.ScalarAsync<bool>($"SELECT IsEnrolled FROM UserTotpCredentials WHERE UserId = N'{user.Value}';")).Should().BeFalse();
        (await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId = N'{user.Value}';")).Should().Be(0);
        (await w.AnyRowsAsync(user.Value)).Should().Be(0, "nothing committed, nothing recorded");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_first_enrolment_waits_for_another_change_to_administrator_access(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var user = new UserId(await w.EntraIndependentAdminAsync("user"));
        NativeMfaEnrollmentChallenge challenge;
        await using (var db = w.Scratch.NewDb())
            challenge = await w.Mfa(db).BeginEnrollmentAsync(user, "user");

        await using (var holder = await HoldTheGateAsync(w.Scratch))
        {
            var complete = Task.Run(async () =>
            {
                await using var db = w.Scratch.NewDb();
                return await w.Mfa(db).CompleteEnrollmentAsync(user, Rfc6238Totp.Compute(Base32.Decode(challenge.ManualEntryKey), DateTimeOffset.UtcNow.ToUnixTimeSeconds()), AuditActor.EmergencyCli);
            });
            (await WaitsOnTheGateAsync(w.Scratch, complete)).Should().BeTrue();
            await holder.RollbackAsync();
            (await complete.WaitAsync(Patience)).Should().HaveCount(10);
        }

        (await w.RowsAsync(AuditActionNames.MfaEnrolled, user.Value)).Should().ContainSingle();
    }

    // ================================================================================================ the bare reset

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_bare_reset_that_takes_a_healthy_posture_away_with_no_entra_independent_administrator_is_refused_and_rolled_back(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "entra", attest: ServiceViabilityWorld.Established(), registration: true);
        var active = await ActiveAsync(e);

        Exception? refusal;
        await using (var db = e.W.Scratch.NewDb())
            refusal = await Record.ExceptionAsync(() => e.W.Mfa(db).ResetAsync(e.User, AuditActor.EmergencyCli).AsTask());

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        (await ActiveAsync(e)).Should().Be(active, "the authenticator and its codes are exactly what they were");
        (await e.W.RowsAsync(AuditActionNames.MfaReset, e.Id)).Should().BeEmpty();
        var row = (await e.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, e.Id)).Should().ContainSingle().Which;
        row.Fields["operation"].Should().Be("mfa.reset");
        row.Fields["code"].Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        row.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_bare_reset_named_with_a_differently_cased_id_is_decided_for_the_stored_recovery_account(bool rcsi)
    {
        // D-1: had the decision been given the caller's spelling, it would not have recognised the Recovery Access account and would have
        // allowed the reset on the posture alone.
        var e = await RecoveryAsync(rcsi, fallback: "entra", attest: ServiceViabilityWorld.Established(), registration: true);
        var active = await ActiveAsync(e);

        Exception? refusal;
        await using (var db = e.W.Scratch.NewDb())
            refusal = await Record.ExceptionAsync(() => e.W.Mfa(db).ResetAsync(new UserId(e.Id.ToUpperInvariant()), AuditActor.EmergencyCli).AsTask());

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        (await ActiveAsync(e)).Should().Be(active);
        (await e.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, e.Id)).Should().ContainSingle("the row names the stored account");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_bare_reset_that_takes_a_healthy_posture_away_with_an_entra_independent_administrator_stands_with_attested_evidence(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "local", attest: ServiceViabilityWorld.AtStart());

        await using (var db = e.W.Scratch.NewDb())
            await e.W.Mfa(db).ResetAsync(e.User, AuditActor.EmergencyCli);

        (await e.W.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{e.Id}';")).Should().Be(0);
        (await e.W.RowsAsync(AuditActionNames.MfaReset, e.Id)).Should().ContainSingle()
            .Which.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_bare_reset_that_would_reduce_without_a_fresh_attestation_is_refused_as_unavailable(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "local");
        var active = await ActiveAsync(e);

        Exception? refusal;
        await using (var db = e.W.Scratch.NewDb())
            refusal = await Record.ExceptionAsync(() => e.W.Mfa(db).ResetAsync(e.User, AuditActor.EmergencyCli).AsTask());

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AdministratorViabilityUnavailable);
        (await ActiveAsync(e)).Should().Be(active);
        (await e.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, e.Id)).Should().ContainSingle()
            .Which.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Unavailable);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_bare_reset_of_a_posture_that_was_not_healthy_needs_no_evidence_and_records_not_required(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "none");
        await e.W.Scratch.ExecuteAsync($"UPDATE UserMfaRecoveryCodes SET ConsumedAtUtc = SYSDATETIMEOFFSET() WHERE UserId = N'{e.Id}';");

        await using (var db = e.W.Scratch.NewDb())
            await e.W.Mfa(db).ResetAsync(e.User, AuditActor.EmergencyCli);

        (await e.W.RowsAsync(AuditActionNames.MfaReset, e.Id)).Should().ContainSingle()
            .Which.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.NotRequired);
    }

    // ================================================================================================ the replacement

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_replacement_keeps_the_active_authenticator_until_the_switch_then_retires_it_and_spends_the_verification_code(bool rcsi)
    {
        // No attestation at all: a replacement is judged on the posture alone and is always available.
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var (challenge, version, newSecret) = await StageAndVerifyAsync(e);
        var verificationStep = e.Clock.Unix;

        e.Clock.Advance(30);
        (await SignInAsync(e, e.Secret, e.Clock.Unix)).Should().BeTrue("the active authenticator stays usable until the switch");

        var result = await CompleteAsync(e, challenge.CandidateId, version);

        // Still within one step of the verification, so only the recorded step can refuse its code.
        (await SignInAsync(e, newSecret, verificationStep)).Should().BeFalse("the code that verified the candidate is spent: the switch recorded its step");

        result.AlreadyApplied.Should().BeFalse();
        result.RecoveryCodes.Should().HaveCount(10);
        (await CredentialIdAsync(e)).Should().Be(challenge.CandidateId, "the candidate is the active authenticator, under its own id");
        (await CandidateStatusAsync(e, challenge.CandidateId)).Should().Be((int)NativeMfaCandidateStatus.Consumed);
        var replaced = (await e.W.RowsAsync(AuditActionNames.MfaReplaced, e.Id)).Should().ContainSingle().Which;
        replaced.Actor.Should().Be(ActorLiterals.EmergencyCli);
        replaced.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.NotRequired);
        (await e.W.RowsAsync(AuditActionNames.RecoveryCodesGenerated, e.Id)).Count(r => r.Fields["regenerated"] == "true").Should().Be(1);

        e.Clock.Advance(30);
        (await SignInAsync(e, e.Secret, e.Clock.Unix)).Should().BeFalse("the retired authenticator signs in no one");
        (await SignInAsync(e, newSecret, e.Clock.Unix)).Should().BeTrue("the new authenticator signs in");
        (await RecoveryCodeSignInAsync(e, e.Codes[0])).Should().BeFalse("the old recovery codes were replaced");
        (await RecoveryCodeSignInAsync(e, result.RecoveryCodes[0])).Should().BeTrue("the codes shown are the codes stored");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_replacement_named_with_a_differently_cased_id_switches_and_records_the_stored_account(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var spelled = e.Id.ToUpperInvariant();
        spelled.Should().NotBe(e.Id);
        var (challenge, version, _) = await StageAndVerifyAsync(e, spelled);

        await CompleteAsync(e, challenge.CandidateId, version, spelled);

        (await e.W.RowsAsync(AuditActionNames.MfaReplaced, e.Id)).Should().ContainSingle();
        (await e.W.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId COLLATE Latin1_General_BIN2 = N'{e.Id}';")).Should().Be(10,
            "every new code belongs to the user as stored");
        (await e.W.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpReplacementCandidates WHERE UserId COLLATE Latin1_General_BIN2 <> N'{e.Id}';")).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_expired_candidate_does_not_switch_is_removed_and_leaves_the_active_authenticator(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var (challenge, version, _) = await StageAndVerifyAsync(e);
        var active = await ActiveAsync(e);
        await e.W.Scratch.ExecuteAsync($"UPDATE UserTotpReplacementCandidates SET ExpiresAtUtc = DATEADD(SECOND, -2, SYSUTCDATETIME()) WHERE CandidateId = N'{challenge.CandidateId}';");

        var refusal = await Record.ExceptionAsync(() => CompleteAsync(e, challenge.CandidateId, version));

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AuthenticatorReplacementExpired);
        (await ActiveAsync(e)).Should().Be(active);
        (await CandidateRowsAsync(e)).Should().Be(0, "the expired candidate's protected secret is not left at rest");
        (await e.W.RowsAsync(AuditActionNames.MfaReplaced, e.Id)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_superseded_candidate_and_a_stale_version_do_not_switch(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var (first, firstVersion, _) = await StageAndVerifyAsync(e);
        var (second, secondVersion, _) = await StageAndVerifyAsync(e);
        var active = await ActiveAsync(e);

        (await Record.ExceptionAsync(() => CompleteAsync(e, first.CandidateId, firstVersion)))
            .Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid);
        (await Record.ExceptionAsync(() => CompleteAsync(e, second.CandidateId, secondVersion + 5)))
            .Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid);
        (await ActiveAsync(e)).Should().Be(active);

        // The live candidate still switches at its own version.
        (await CompleteAsync(e, second.CandidateId, secondVersion)).AlreadyApplied.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_candidate_whose_codes_were_regenerated_or_whose_authenticator_was_reset_after_verification_does_not_switch(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "local", attest: ServiceViabilityWorld.AtStart());

        var (regenerated, regeneratedVersion, _) = await StageAndVerifyAsync(e);
        await using (var db = e.W.Scratch.NewDb())
            await e.W.Mfa(db).RegenerateRecoveryCodesAsync(e.User, AuditActor.EmergencyCli);
        (await Record.ExceptionAsync(() => CompleteAsync(e, regenerated.CandidateId, regeneratedVersion)))
            .Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AuthenticatorReplacementNoLongerValid,
                "the codes the candidate would have replaced are not the ones the operator saw");

        var (reset, resetVersion, _) = await StageAndVerifyAsync(e);
        await using (var db = e.W.Scratch.NewDb())
            await e.W.Mfa(db).ResetAsync(e.User, AuditActor.EmergencyCli);
        (await Record.ExceptionAsync(() => CompleteAsync(e, reset.CandidateId, resetVersion)))
            .Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AuthenticatorNotEnrolled,
                "there is nothing to replace, and the switch never falls back to an enrolment");
        (await e.W.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{e.Id}';")).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_switch_whose_decision_moved_off_the_posture_because_administrator_access_changed_is_rolled_back_and_can_be_run_again(bool rcsi)
    {
        // I-R3-1. While the switch holds the gate, a change that takes no gate (a Microsoft link proven at sign-in, E-2) moves another
        // administrator's facts. The decision is then not made on the posture alone, and the switch is not kept whatever its verdict.
        var e = await RecoveryAsync(rcsi, fallback: "local", attest: ServiceViabilityWorld.AtStart());
        var other = await e.W.EntraIndependentAdminAsync("linking");
        await e.W.World.AddEntraLinkAsync(other, ServiceViabilityWorld.Tenant, proven: false);
        var (challenge, version, _) = await StageAndVerifyAsync(e);
        var active = await ActiveAsync(e);

        var pause = new PauseAt(c => c.CommandText.Contains(SwitchStatement, StringComparison.Ordinal), after: true);
        var complete = CompleteAsync(e, challenge.CandidateId, version, null, pause);
        await ReachedAsync(pause, complete);
        await e.W.Scratch.ExecuteAsync($"UPDATE IdentityLinks SET LastAuthenticatedAt = SYSDATETIMEOFFSET() WHERE Id = N'E{other}';");
        pause.Release();

        var refusal = await Record.ExceptionAsync(() => complete.WaitAsync(Patience));

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AdministratorAccessChanged);
        (await ActiveAsync(e)).Should().Be(active, "the applied switch was rolled back");
        (await CandidateStatusAsync(e, challenge.CandidateId)).Should().Be((int)NativeMfaCandidateStatus.Verified, "the candidate is verified again, at its version");
        var row = (await e.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, e.Id)).Should().ContainSingle().Which;
        row.Fields["operation"].Should().Be("mfa.replace");
        row.Fields["code"].Should().Be(GovernanceConflictCodes.AdministratorAccessChanged);
        row.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested,
            "the discarded decision was the full evaluation, proven safe on the attested Local reading; the row names that evidence");

        // Run again on the facts as they now stand: decided on the posture alone, and kept.
        (await CompleteAsync(e, challenge.CandidateId, version)).AlreadyApplied.Should().BeFalse();
        (await CredentialIdAsync(e)).Should().Be(challenge.CandidateId);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_switch_that_fails_after_its_first_writes_commits_nothing_and_the_candidate_still_switches_afterwards(bool rcsi)
    {
        // S-2: a statement of the switch fails after the credential was rewritten and the candidate consumed; the unit must not commit.
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var (challenge, version, _) = await StageAndVerifyAsync(e);
        var active = await ActiveAsync(e);
        var fail = new FailingCommand(sql => sql.Contains("DELETE FROM [UserMfaRecoveryCodes]", StringComparison.Ordinal));

        var failure = await Record.ExceptionAsync(() => CompleteAsync(e, challenge.CandidateId, version, null, fail));

        failure.Should().NotBeNull();
        fail.Failures.Should().BeGreaterThan(0);
        (await ActiveAsync(e)).Should().Be(active, "nothing of the half-applied switch was kept");
        (await CandidateStatusAsync(e, challenge.CandidateId)).Should().Be((int)NativeMfaCandidateStatus.Verified);
        (await e.W.RowsAsync(AuditActionNames.MfaReplaced, e.Id)).Should().BeEmpty();
        (await CompleteAsync(e, challenge.CandidateId, version)).AlreadyApplied.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_switch_whose_commit_acknowledgement_is_lost_returns_the_codes_it_stored_once_verification_confirms_the_commit(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var (challenge, version, _) = await StageAndVerifyAsync(e);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        var result = await CompleteAsync(e, challenge.CandidateId, version, null, lost, attempts);

        attempts.Started.Should().Be(1, "verification confirmed the commit, so nothing reran");
        result.AlreadyApplied.Should().BeFalse();
        (await RecoveryCodeSignInAsync(e, result.RecoveryCodes[0])).Should().BeTrue("the codes returned are the codes the committed switch stored");
        (await e.W.RowsAsync(AuditActionNames.MfaReplaced, e.Id)).Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_switch_whose_outcome_is_unknown_is_reported_unknown_and_a_repeat_finds_it_applied_without_claiming_its_codes(bool rcsi)
    {
        // S-1: the commit happened, the acknowledgement was lost and verification could not run. The caller is told the outcome is unknown,
        // never that nothing changed; a repeat finds the candidate consumed and the active authenticator to be its own, and says so.
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var (challenge, version, _) = await StageAndVerifyAsync(e);

        Exception? unknown;
        await using (var db = e.W.Scratch.NewDb(new LostCommitAcknowledgement()))
        {
            var unit = NewUnit(db, null, verification: new UnreachableVerification());
            unknown = await Record.ExceptionAsync(() =>
                e.W.Mfa(db, unit, time: e.Clock).CompleteReplacementAsync(e.User, challenge.CandidateId, version, AuditActor.EmergencyCli).AsTask());
        }

        unknown.Should().BeOfType<AuditOutcomeUnknownException>();
        (await CredentialIdAsync(e)).Should().Be(challenge.CandidateId, "the switch did commit");

        var repeat = await CompleteAsync(e, challenge.CandidateId, version);
        repeat.AlreadyApplied.Should().BeTrue("a consumed candidate is reported applied only because the active authenticator is its own");
        repeat.RecoveryCodes.Should().BeEmpty();
        (await e.W.RowsAsync(AuditActionNames.MfaReplaced, e.Id)).Should().ContainSingle("the repeat recorded nothing");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_switch_whose_commit_was_rolled_back_runs_again_and_applies_once(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var (challenge, version, _) = await StageAndVerifyAsync(e);
        var attempts = new AttemptCounter();

        var result = await CompleteAsync(e, challenge.CandidateId, version, null, new RolledBackCommit(), attempts);

        attempts.Started.Should().Be(2, "verification confirmed the rollback, and only then did the operation run again");
        result.AlreadyApplied.Should().BeFalse();
        (await CredentialIdAsync(e)).Should().Be(challenge.CandidateId);
        (await e.W.RowsAsync(AuditActionNames.MfaReplaced, e.Id)).Should().ContainSingle();
        (await RecoveryCodeSignInAsync(e, result.RecoveryCodes[0])).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Two_switches_of_one_candidate_apply_once_and_the_second_is_told_it_was_applied(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var (challenge, version, _) = await StageAndVerifyAsync(e);

        var pause = new PauseAt(c => c.CommandText.Contains(SwitchStatement, StringComparison.Ordinal), after: true);
        var first = CompleteAsync(e, challenge.CandidateId, version, null, pause);
        await ReachedAsync(pause, first);
        var second = CompleteAsync(e, challenge.CandidateId, version);
        (await WaitsOnTheGateAsync(e.W.Scratch, second)).Should().BeTrue();
        pause.Release();

        var results = new[] { await first.WaitAsync(Patience), await second.WaitAsync(Patience) };

        results[0].AlreadyApplied.Should().BeFalse();
        results[1].AlreadyApplied.Should().BeTrue();
        (await e.W.RowsAsync(AuditActionNames.MfaReplaced, e.Id)).Should().ContainSingle();
    }

    // ================================================================================================ Recovery Access and MFA together

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_revocation_waits_for_a_replacement_that_holds_the_gate_and_both_apply_in_order(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "local", attest: ServiceViabilityWorld.AtStart());
        var (challenge, version, _) = await StageAndVerifyAsync(e);

        var pause = new PauseAt(c => c.CommandText.Contains(SwitchStatement, StringComparison.Ordinal), after: true);
        var replace = CompleteAsync(e, challenge.CandidateId, version, null, pause);
        await ReachedAsync(pause, replace);
        var revoke = Task.Run(async () =>
        {
            await using var db = e.W.Scratch.NewDb();
            await e.W.Emergency(db).RevokeAsync(confirmFallback: true, Cli);
        });
        (await WaitsOnTheGateAsync(e.W.Scratch, revoke)).Should().BeTrue();
        pause.Release();

        (await replace.WaitAsync(Patience)).AlreadyApplied.Should().BeFalse();
        await revoke.WaitAsync(Patience);
        (await CredentialIdAsync(e)).Should().Be(challenge.CandidateId);
        (await e.W.DesignatedAsync(e.Id)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reset_waits_for_a_designation_that_holds_the_gate_and_is_decided_on_what_it_committed(bool rcsi)
    {
        // The reset is of the account that is Recovery Access before the designation and is not after it. Decided under the gate on the
        // committed designation, it is a reset of an ordinary account's authenticator, which the posture alone allows.
        var e = await RecoveryAsync(rcsi, fallback: "local", attest: ServiceViabilityWorld.AtStart());
        var next = await e.W.EntraIndependentAdminAsync("next");
        await e.W.EntraIndependentAdminAsync("third");

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var designate = Task.Run(async () =>
        {
            await using var db = e.W.Scratch.NewDb(pause);
            await e.W.Emergency(db).DesignateAsync(new UserId(next), Cli);
        });
        await ReachedAsync(pause, designate);
        var reset = Task.Run(async () =>
        {
            await using var db = e.W.Scratch.NewDb();
            await e.W.Mfa(db).ResetAsync(e.User, AuditActor.EmergencyCli);
        });
        (await WaitsOnTheGateAsync(e.W.Scratch, reset)).Should().BeTrue();
        pause.Release();

        await designate.WaitAsync(Patience);
        await reset.WaitAsync(Patience);
        (await e.W.DesignatedAsync(next)).Should().BeTrue();
        (await e.W.RowsAsync(AuditActionNames.MfaReset, e.Id)).Should().ContainSingle()
            .Which.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.NotRequired,
                "decided on the committed designation: not the Recovery Access account any more");
    }

    // ================================================================================================ the switch's own defences (S-3, S-2)

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_switch_refuses_a_differently_cased_user_used_codes_and_repeated_code_ids_before_writing_anything(bool rcsi)
    {
        var e = await RecoveryAsync(rcsi, fallback: "none");
        var (challenge, version, _) = await StageAndVerifyAsync(e);
        var active = await ActiveAsync(e);

        NativeMfaRecoveryCodeRecord Code(string id, UserId owner, DateTimeOffset? consumed = null) => new(id, owner, "hash-" + id, "salt", "test", consumed);
        async Task<Exception?> SwitchAsync(UserId user, IReadOnlyList<NativeMfaRecoveryCodeRecord> codes)
        {
            await using var db = e.W.Scratch.NewDb();
            return await Record.ExceptionAsync(() => new SqlNativeMfaReplacementCandidateRepository(db)
                .SwitchAsync(new NativeMfaSwitchRequest(user, challenge.CandidateId, version, codes, DateTimeOffset.UtcNow)).AsTask());
        }

        var upper = new UserId(e.Id.ToUpperInvariant());
        (await SwitchAsync(upper, [Code("c1", upper)])).Should().BeOfType<ArgumentException>("the user must be named as stored");
        (await SwitchAsync(e.User, [Code("c2", e.User, DateTimeOffset.UtcNow)])).Should().BeOfType<ArgumentException>("a switch issues unused codes");
        (await SwitchAsync(e.User, [Code("dup", e.User), Code("DUP", e.User)])).Should().BeOfType<ArgumentException>("each code has an id of its own");

        (await ActiveAsync(e)).Should().Be(active);
        (await CandidateStatusAsync(e, challenge.CandidateId)).Should().Be((int)NativeMfaCandidateStatus.Verified);
    }
}
