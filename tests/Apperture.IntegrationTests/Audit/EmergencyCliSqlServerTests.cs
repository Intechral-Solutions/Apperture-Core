using System.Text.RegularExpressions;
using Apperture.Cli.Host;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The emergency command line against real SQL Server on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT: the real command runner over
/// the production services (the audited unit, the global gate, the attested configuration reader, the candidate store), one context per
/// command as the process has. Each case states the exit status, what the operator was shown, and, from SQL of its own, what the database
/// holds afterwards. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class EmergencyCliSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public EmergencyCliSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ------------------------------------------------------------------------------------------------ arrangement

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public long Unix => _now.ToUnixTimeSeconds();

        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    /// <summary>An enrolled Recovery Access account: the secret its authenticator app holds and the recovery codes it was shown.</summary>
    private sealed record Account(ServiceViabilityWorld W, UserId User, byte[] Secret, IReadOnlyList<string> Codes, Clock Clock)
    {
        public string Id => User.Value;
    }

    private async Task<Account> ArrangeAsync(bool rcsi, string fallback = "local", AttestedAuthenticationPayload? attest = null, bool registration = false)
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

    private static async Task<Account> EnrollAsync(ServiceViabilityWorld w, UserId user)
    {
        var clock = new Clock();
        await using var db = w.Scratch.NewDb();
        var mfa = w.Mfa(db, time: clock);
        var challenge = await mfa.BeginEnrollmentAsync(user, "account");
        var secret = Base32.Decode(challenge.ManualEntryKey);
        var codes = await mfa.CompleteEnrollmentAsync(user, Rfc6238Totp.Compute(secret, clock.Unix), AuditActor.EmergencyCli);
        return new Account(w, user, secret, codes, clock);
    }

    /// <summary>The active authenticator and its codes, as a value a command that changed nothing must leave equal.</summary>
    private static Task<string> ActiveAsync(Account a) =>
        a.W.Scratch.ScalarAsync<string>($"""
            SELECT CONCAT(
                (SELECT CONCAT_WS(N'|', CredentialId, CONVERT(nvarchar(64), HASHBYTES('SHA2_256', ProtectedSecret), 2), CONVERT(nvarchar(5), IsEnrolled), CONVERT(nvarchar(40), LastUsedStep))
                   FROM UserTotpCredentials WHERE UserId = N'{a.Id}'),
                N'#',
                (SELECT STRING_AGG(CONCAT_WS(N':', Id, CodeHash, CONVERT(nvarchar(40), ConsumedAtUtc, 127)), N',') WITHIN GROUP (ORDER BY Id)
                   FROM UserMfaRecoveryCodes WHERE UserId = N'{a.Id}'));
            """);

    private static Task<int> CandidateRowsAsync(Account a) =>
        a.W.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpReplacementCandidates WHERE UserId = N'{a.Id}';");

    private static Task<int> AuthenticatorRowsAsync(Account a) =>
        a.W.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{a.Id}';");

    /// <summary>Every plaintext recovery code or authenticator secret found in any audit row's details (there must be none).</summary>
    private static async Task<int> AuditRowsContainingAsync(Account a, string needle) =>
        await a.W.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Details LIKE N'%{needle.Replace("'", "''")}%' OR TargetId LIKE N'%{needle.Replace("'", "''")}%';");

    // ------------------------------------------------------------------------------------------------ the command line

    /// <summary>A console that scripts the operator: the code it types may depend on what was shown, and runs code of the test at the prompt.</summary>
    private sealed class OperatorConsole(bool redirected, Func<OperatorConsole, int, Task<string?>>? type) : IEmergencyConsole
    {
        public TextWriter Out { get; } = new StringWriter();

        public TextWriter Error { get; } = new StringWriter();

        public bool IsInputRedirected => redirected;

        public int Reads { get; private set; }

        public string Shown => Out.ToString()!;

        public string Complaints => Error.ToString()!;

        public async Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellationToken)
        {
            Reads++;
            return type is null ? null : await type(this, Reads);
        }

        /// <summary>The manual key the command showed for the staged authenticator.</summary>
        public byte[] ShownKey()
        {
            var match = Regex.Match(Shown, @"Manual key: (\S+)");
            match.Success.Should().BeTrue("the command shows the key before it asks for a code");
            return Base32.Decode(match.Groups[1].Value);
        }

        public IReadOnlyList<string> ShownCodes()
        {
            var lines = Shown.Split('\n', StringSplitOptions.TrimEntries);
            var marker = Array.FindIndex(lines, l => l.StartsWith("Store these recovery codes now", StringComparison.Ordinal));
            return marker < 0 ? [] : lines.Skip(marker + 1).Where(l => l.Length > 0).ToList();
        }
    }

    private sealed record CliRun(int Exit, OperatorConsole Console);

    /// <summary>Types the code of the authenticator the command staged, a step later than anything the account used.</summary>
    private static Func<OperatorConsole, int, Task<string?>> TypesTheNewCode(Account a) =>
        (console, _) => Task.FromResult<string?>(Rfc6238Totp.Compute(console.ShownKey(), a.Clock.Unix));

    private static async Task<CliRun> RunAsync(
        Account a,
        string[] args,
        Func<OperatorConsole, int, Task<string?>>? type = null,
        bool redirected = false,
        TimeSpan? gateWait = null,
        Func<INativeMfaService, INativeMfaService>? decorate = null,
        IAuditVerificationConnectionFactory? verification = null,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        // The account is named with the option every command that acts on one takes; status takes none.
        string[] line = args[0] == "status" ? args : [.. args, "--user-id", a.Id];
        EmergencyCommandLine.TryParse(line, out var command, out var error).Should().BeTrue(error);

        // One context for the whole command, as the process has one scope: stage, verify and switch all run on it.
        await using var db = a.W.Scratch.NewDb(interceptors);
        var unit = verification is null ? null : AuditRecorderTestSupport.NewUnit(db, null, verification: verification);
        var coordinator = new AdministratorViabilityCoordinator(
            new SqlPlatformAdministratorSetGate(db, gateWait),
            new AdministratorViabilityService(
                new SqlAdministratorFactsReader(db),
                new EffectiveSignInConfigurationProvider(
                    new AttestedDeploymentAuthenticationConfiguration(new SqlHostConfigurationAttestationStore(db)),
                    new SqlEntraInstanceRegistrationRepository(db))));
        INativeMfaService mfa = a.W.Mfa(db, unit, coordinator, a.Clock);
        if (decorate is not null)
            mfa = decorate(mfa);
        var emergency = a.W.Emergency(db, unit, coordinator);
        var users = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);

        var console = new OperatorConsole(redirected, type);
        var exit = await new EmergencyCommandRunner(mfa, emergency, users, console).RunAsync(command!, CancellationToken.None);
        return new CliRun(exit, console);
    }

    /// <summary>The state a switch from another process, that is not the command line, leaves behind.</summary>
    private static async Task<NativeMfaReplacementResult> OtherOperatorReplacesAsync(Account a)
    {
        a.Clock.Advance(60);
        await using var db = a.W.Scratch.NewDb();
        var mfa = a.W.Mfa(db, time: a.Clock);
        var challenge = await mfa.BeginReplacementAsync(a.User, "account");
        var key = Base32.Decode(challenge.ManualEntryKey);
        var verified = await mfa.VerifyReplacementAsync(a.User, challenge.CandidateId, challenge.Version, Rfc6238Totp.Compute(key, a.Clock.Unix));
        return await mfa.CompleteReplacementAsync(a.User, challenge.CandidateId, verified.Version, AuditActor.EmergencyCli);
    }

    // ================================================================================================ the replacement, the normal workflow

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_replacement_commits_with_its_audit_rows_shows_the_new_codes_once_and_replaces_every_old_secret(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);

        var run = await RunAsync(a, ["reset-mfa"], TypesTheNewCode(a));

        run.Exit.Should().Be(EmergencyExitCodes.Success, run.Console.Complaints);
        var newSecret = run.Console.ShownKey();
        var newCodes = run.Console.ShownCodes();
        newCodes.Should().HaveCount(10);
        newCodes.Intersect(a.Codes).Should().BeEmpty("the shown codes are the new ones");

        // The database agrees with what was shown: the new authenticator and new codes work, the old ones do not.
        a.Clock.Advance(60);
        await using (var db = a.W.Scratch.NewDb())
        {
            var mfa = a.W.Mfa(db, time: a.Clock);
            (await mfa.VerifyTotpAsync(a.User, Rfc6238Totp.Compute(newSecret, a.Clock.Unix))).Should().BeTrue();
            a.Clock.Advance(60);
            (await mfa.VerifyTotpAsync(a.User, Rfc6238Totp.Compute(a.Secret, a.Clock.Unix))).Should().BeFalse("the previous authenticator no longer signs in");
            (await mfa.ConsumeRecoveryCodeAsync(a.User, a.Codes[0])).Should().BeFalse("a previous recovery code no longer works");
            (await mfa.ConsumeRecoveryCodeAsync(a.User, newCodes[0])).Should().BeTrue("a shown recovery code works");
        }

        (await a.W.RowsAsync(AuditActionNames.MfaReplaced, a.Id)).Should().ContainSingle()
            .Which.Actor.Should().NotBeNullOrEmpty();
        (await a.W.RowsAsync(AuditActionNames.RecoveryCodesGenerated, a.Id)).Should().NotBeEmpty();
        (await a.W.RowsAsync(AuditActionNames.MfaReset, a.Id)).Should().BeEmpty("a replacement is not a reset");
        (await CandidateRowsAsync(a)).Should().BeLessThanOrEqualTo(1);
        foreach (var secret in new[] { Convert.ToBase64String(newSecret), Base32.Encode(newSecret) }.Concat(newCodes))
            (await AuditRowsContainingAsync(a, secret)).Should().Be(0, "no secret or recovery code is recorded");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_current_authenticator_and_its_codes_are_untouched_while_the_operator_is_at_the_prompt(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);
        var before = await ActiveAsync(a);
        string? atThePrompt = null;
        string? gateAtThePrompt = null;

        var run = await RunAsync(a, ["reset-mfa"], async (console, _) =>
        {
            atThePrompt = await ActiveAsync(a);
            gateAtThePrompt = (await ProbeGateAsync(a.W.Scratch)).ToString();
            return Rfc6238Totp.Compute(console.ShownKey(), a.Clock.Unix);
        });

        run.Exit.Should().Be(EmergencyExitCodes.Success);
        atThePrompt.Should().Be(before, "nothing about the active authenticator changes before the switch");
        int.Parse(gateAtThePrompt!).Should().BeGreaterThanOrEqualTo(0, "no administrator-set gate or transaction is held while waiting for the operator");
        (await ActiveAsync(a)).Should().NotBe(before);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Abandoning_the_replacement_by_ending_input_leaves_the_active_authenticator_and_removes_the_candidate(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        var before = await ActiveAsync(a);

        var run = await RunAsync(a, ["reset-mfa"], type: null, redirected: true);

        run.Exit.Should().Be(EmergencyExitCodes.InvalidUsage);
        (await ActiveAsync(a)).Should().Be(before);
        (await CandidateRowsAsync(a)).Should().Be(0, "the abandoned candidate's protected secret is not left at rest");
        (await a.W.RowsAsync(AuditActionNames.MfaReplaced, a.Id)).Should().BeEmpty();
        run.Console.ShownCodes().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_wrong_code_every_time_discards_the_candidate_and_changes_nothing(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);
        var before = await ActiveAsync(a);

        // The code of the account's current authenticator is the wrong code: the check is against the new key.
        var run = await RunAsync(a, ["reset-mfa"], (_, _) => Task.FromResult<string?>(Rfc6238Totp.Compute(a.Secret, a.Clock.Unix)));

        run.Exit.Should().Be(EmergencyExitCodes.InvalidUsage);
        run.Console.Reads.Should().Be(EmergencyCommandRunner.MaxCodeAttempts);
        (await ActiveAsync(a)).Should().Be(before);
        (await CandidateRowsAsync(a)).Should().Be(0);
        run.Console.ShownCodes().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_expired_candidate_is_a_retryable_conflict_removes_the_candidate_and_switches_nothing(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);
        var before = await ActiveAsync(a);

        var run = await RunAsync(a, ["reset-mfa"], async (console, _) =>
        {
            await a.W.Scratch.ExecuteAsync($"UPDATE UserTotpReplacementCandidates SET ExpiresAtUtc = DATEADD(SECOND, -2, SYSUTCDATETIME()) WHERE UserId = N'{a.Id}';");
            return Rfc6238Totp.Compute(console.ShownKey(), a.Clock.Unix);
        });

        run.Exit.Should().Be(EmergencyExitCodes.Retryable);
        run.Console.Complaints.Should().Contain("[authenticator_replacement_expired]").And.Contain("still active");
        (await ActiveAsync(a)).Should().Be(before);
        (await CandidateRowsAsync(a)).Should().Be(0);
        run.Console.ShownCodes().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_replacement_another_operator_started_first_supersedes_this_one_and_this_one_switches_nothing(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);

        string? afterOther = null;
        var run = await RunAsync(a, ["reset-mfa"], async (console, _) =>
        {
            // While this operator is at the prompt another operator replaces the authenticator.
            await OtherOperatorReplacesAsync(a);
            afterOther = await ActiveAsync(a);
            return Rfc6238Totp.Compute(console.ShownKey(), a.Clock.Unix);
        });

        run.Exit.Should().Be(EmergencyExitCodes.Retryable);
        run.Console.Complaints.Should().Contain("[authenticator_replacement_no_longer_valid]");
        (await ActiveAsync(a)).Should().Be(afterOther, "the command changed nothing on top of the other operator's replacement");
        (await a.W.RowsAsync(AuditActionNames.MfaReplaced, a.Id)).Should().ContainSingle("only the other operator's replacement was recorded");
        run.Console.ShownCodes().Should().BeEmpty();
    }

    /// <summary>Runs something of the test between this command's verification of the code and its switch (another operator, or arming a failure).</summary>
    private sealed class SwitchedBeforeUs(INativeMfaService inner, Func<NativeMfaReplacementVerification, Task> afterVerify) : INativeMfaService
    {
        public ValueTask<bool> IsEnrolledAsync(UserId userId, CancellationToken ct = default) => inner.IsEnrolledAsync(userId, ct);

        public ValueTask<bool> IsRequiredAsync(User user, CancellationToken ct = default) => inner.IsRequiredAsync(user, ct);

        public ValueTask<NativeMfaEnrollmentChallenge> BeginEnrollmentAsync(UserId userId, string accountName, CancellationToken ct = default) => inner.BeginEnrollmentAsync(userId, accountName, ct);

        public ValueTask<IReadOnlyList<string>> CompleteEnrollmentAsync(UserId userId, string totpCode, AuditActor actor, CancellationToken ct = default) => inner.CompleteEnrollmentAsync(userId, totpCode, actor, ct);

        public ValueTask<bool> VerifyTotpAsync(UserId userId, string totpCode, CancellationToken ct = default) => inner.VerifyTotpAsync(userId, totpCode, ct);

        public ValueTask<bool> ConsumeRecoveryCodeAsync(UserId userId, string recoveryCode, CancellationToken ct = default) => inner.ConsumeRecoveryCodeAsync(userId, recoveryCode, ct);

        public ValueTask<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(UserId userId, AuditActor actor, CancellationToken ct = default) => inner.RegenerateRecoveryCodesAsync(userId, actor, ct);

        public ValueTask ResetAsync(UserId userId, AuditActor actor, CancellationToken ct = default) => inner.ResetAsync(userId, actor, ct);

        public ValueTask<int> CountUnusedRecoveryCodesAsync(UserId userId, CancellationToken ct = default) => inner.CountUnusedRecoveryCodesAsync(userId, ct);

        public ValueTask<NativeMfaReplacementChallenge> BeginReplacementAsync(UserId userId, string accountName, CancellationToken ct = default) => inner.BeginReplacementAsync(userId, accountName, ct);

        public async ValueTask<NativeMfaReplacementVerification> VerifyReplacementAsync(UserId userId, string candidateId, int version, string totpCode, CancellationToken ct = default)
        {
            var verified = await inner.VerifyReplacementAsync(userId, candidateId, version, totpCode, ct);
            await afterVerify(verified);
            return verified;
        }

        public ValueTask<NativeMfaReplacementResult> CompleteReplacementAsync(UserId userId, string candidateId, int version, AuditActor actor, CancellationToken ct = default) => inner.CompleteReplacementAsync(userId, candidateId, version, actor, ct);

        public ValueTask<bool> AbandonReplacementAsync(UserId userId, CancellationToken ct = default) => inner.AbandonReplacementAsync(userId, ct);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_candidate_already_switched_by_an_earlier_request_is_reported_as_applied_and_shows_no_codes(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);

        // The very candidate this command verified is switched by an earlier request (whose result never arrived), then the command switches it again.
        var run = await RunAsync(a, ["reset-mfa"], TypesTheNewCode(a), decorate: inner => new SwitchedBeforeUs(inner, async verified =>
        {
            await using var db = a.W.Scratch.NewDb();
            var candidate = await a.W.Scratch.ScalarAsync<string>($"SELECT CandidateId FROM UserTotpReplacementCandidates WHERE UserId = N'{a.Id}';");
            await a.W.Mfa(db, time: a.Clock).CompleteReplacementAsync(a.User, candidate, verified.Version, AuditActor.EmergencyCli);
        }));

        run.Exit.Should().Be(EmergencyExitCodes.Success, run.Console.Complaints);
        run.Console.Shown.Should().Contain("already been applied").And.Contain("cannot be shown again");
        run.Console.ShownCodes().Should().BeEmpty();
        (await a.W.RowsAsync(AuditActionNames.MfaReplaced, a.Id)).Should().ContainSingle("the switch was applied once, never twice");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_busy_gate_is_a_retryable_conflict_that_changes_nothing_and_shows_no_codes_and_a_later_run_succeeds(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);
        var before = await ActiveAsync(a);

        CliRun busy;
        await using (var holder = await HoldTheGateAsync(a.W.Scratch))
            busy = await RunAsync(a, ["reset-mfa"], TypesTheNewCode(a), gateWait: TimeSpan.FromMilliseconds(500));

        busy.Exit.Should().Be(EmergencyExitCodes.Retryable);
        busy.Console.Complaints.Should().Contain($"[{PlatformAdministratorSetBusyException.BusyCode}]").And.Contain("Nothing was changed");
        busy.Console.ShownCodes().Should().BeEmpty();
        (await ActiveAsync(a)).Should().Be(before);
        (await a.W.RowsAsync(AuditActionNames.MfaReplaced, a.Id)).Should().BeEmpty();

        a.Clock.Advance(60);
        var retry = await RunAsync(a, ["reset-mfa"], TypesTheNewCode(a));
        retry.Exit.Should().Be(EmergencyExitCodes.Success, retry.Console.Complaints);
        retry.Console.ShownCodes().Should().HaveCount(10);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_commit_confirmed_as_rolled_back_is_retryable_shows_no_codes_and_leaves_the_authenticator(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);
        var before = await ActiveAsync(a);

        // From the switch on, the commit never happens, on every attempt the unit makes: verification finds nothing committed, and the
        // reruns are bounded. (Armed after the code is verified: staging and verifying commit on the same context.)
        var rolledBack = new AuditRecorderTestSupport.RolledBackCommit { Remaining = 0 };
        var run = await RunAsync(
            a,
            ["reset-mfa"],
            TypesTheNewCode(a),
            decorate: inner => new SwitchedBeforeUs(inner, _ =>
            {
                rolledBack.Remaining = 50;
                return Task.CompletedTask;
            }),
            interceptors: rolledBack);

        run.Exit.Should().Be(EmergencyExitCodes.Retryable, run.Console.Complaints);
        run.Console.Complaints.Should().Contain("Nothing was changed");
        run.Console.ShownCodes().Should().BeEmpty();
        run.Console.Shown.Should().NotContain("was replaced");
        (await ActiveAsync(a)).Should().Be(before);
        (await a.W.RowsAsync(AuditActionNames.MfaReplaced, a.Id)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_commit_whose_acknowledgement_is_lost_and_cannot_be_verified_is_an_unknown_outcome_with_no_codes_shown(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);

        // The commit reaches the server, the acknowledgement is lost, and the verification that would settle it cannot run either.
        var lost = new AuditRecorderTestSupport.LostCommitAcknowledgement { Remaining = 0 };
        var run = await RunAsync(
            a,
            ["reset-mfa"],
            TypesTheNewCode(a),
            decorate: inner => new SwitchedBeforeUs(inner, _ =>
            {
                lost.Remaining = 50;
                return Task.CompletedTask;
            }),
            verification: new AuditRecorderTestSupport.UnreachableVerification(),
            interceptors: lost);

        run.Exit.Should().Be(EmergencyExitCodes.OutcomeUnknown, run.Console.Complaints);
        run.Console.Complaints.Should().Contain("may or may not have been applied").And.Contain("emergency status").And.NotContain("Nothing was changed");
        run.Console.ShownCodes().Should().BeEmpty("codes are shown only when the commit is confirmed");
        (await a.W.RowsAsync(AuditActionNames.MfaReplaced, a.Id)).Should().ContainSingle("the commit did reach the database: the operator was right not to be told it failed");
        run.Console.Shown.Should().NotContain("Store these recovery codes");
    }

    // ================================================================================================ the bare reset

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_bare_reset_with_a_local_fallback_administrator_removes_the_authenticator_and_codes_and_records_it(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi, fallback: "local", attest: ServiceViabilityWorld.AtStart());

        var run = await RunAsync(a, ["reset-mfa", "--bare-reset"]);

        run.Exit.Should().Be(EmergencyExitCodes.Success, run.Console.Complaints);
        run.Console.Reads.Should().Be(0);
        (await AuthenticatorRowsAsync(a)).Should().Be(0);
        (await a.W.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId = N'{a.Id}';")).Should().Be(0);
        (await a.W.RowsAsync(AuditActionNames.MfaReset, a.Id)).Should().ContainSingle();
        (await CandidateRowsAsync(a)).Should().Be(0, "a bare reset does not stage a replacement");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_bare_reset_without_a_fallback_that_signs_in_without_microsoft_is_refused_with_the_typed_refusal_and_nothing_else_runs(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi, fallback: "entra", attest: ServiceViabilityWorld.Established(), registration: true);
        var before = await ActiveAsync(a);

        var run = await RunAsync(a, ["reset-mfa", "--bare-reset"]);

        run.Exit.Should().Be(EmergencyExitCodes.Refused);
        run.Console.Complaints.Should().Contain($"[{GovernanceConflictCodes.EntraIndependentFallbackRequired}]").And.Contain("Replace the authenticator instead of resetting it");
        (await ActiveAsync(a)).Should().Be(before);
        (await CandidateRowsAsync(a)).Should().Be(0, "the refusal is never answered by a replacement");
        (await a.W.RowsAsync(AuditActionNames.MfaReset, a.Id)).Should().BeEmpty();
        (await a.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, a.Id)).Should().ContainSingle()
            .Which.Fields["code"].Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_bare_reset_without_the_host_attestation_is_refused_as_evidence_unavailable_and_nothing_changes(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi, fallback: "local");
        var before = await ActiveAsync(a);

        var run = await RunAsync(a, ["reset-mfa", "--bare-reset"]);

        run.Exit.Should().Be(EmergencyExitCodes.EvidenceUnavailable);
        run.Console.Complaints.Should().Contain($"[{GovernanceConflictCodes.AdministratorViabilityUnavailable}]").And.Contain("Management Host");
        (await ActiveAsync(a)).Should().Be(before);
        (await a.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, a.Id)).Should().ContainSingle()
            .Which.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Unavailable);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_replacement_needs_no_attestation_because_it_is_judged_on_the_posture_alone(bool rcsi)
    {
        // The same account the bare reset above cannot touch without evidence is replaced without it.
        var a = await ArrangeAsync(rcsi, fallback: "local");
        a.Clock.Advance(60);

        var run = await RunAsync(a, ["reset-mfa"], TypesTheNewCode(a));

        run.Exit.Should().Be(EmergencyExitCodes.Success, run.Console.Complaints);
        run.Console.ShownCodes().Should().HaveCount(10);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_replacement_of_an_account_with_no_authenticator_is_refused_and_never_enrols_or_resets(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        a.Clock.Advance(60);
        await a.W.Scratch.ExecuteAsync($"DELETE FROM UserMfaRecoveryCodes WHERE UserId = N'{a.Id}'; DELETE FROM UserTotpCredentials WHERE UserId = N'{a.Id}';");

        var run = await RunAsync(a, ["reset-mfa"], TypesTheNewCode(a));

        run.Exit.Should().Be(EmergencyExitCodes.Refused);
        run.Console.Complaints.Should().Contain("[authenticator_not_enrolled]").And.Contain("enroll-mfa");
        (await AuthenticatorRowsAsync(a)).Should().Be(0, "a refused replacement does not enrol");
        run.Console.Reads.Should().Be(0);
    }

    // ================================================================================================ the rest of the surface

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Regeneration_shows_the_new_codes_only_from_the_committed_change_and_replaces_the_old_ones(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);

        var run = await RunAsync(a, ["regenerate-codes"]);

        run.Exit.Should().Be(EmergencyExitCodes.Success, run.Console.Complaints);
        var codes = run.Console.ShownCodes();
        codes.Should().HaveCount(10);
        await using var db = a.W.Scratch.NewDb();
        var mfa = a.W.Mfa(db, time: a.Clock);
        (await mfa.ConsumeRecoveryCodeAsync(a.User, a.Codes[0])).Should().BeFalse();
        (await mfa.ConsumeRecoveryCodeAsync(a.User, codes[0])).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Regeneration_with_a_busy_gate_is_retryable_and_shows_no_codes(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        var before = await ActiveAsync(a);

        CliRun run;
        await using (var holder = await HoldTheGateAsync(a.W.Scratch))
            run = await RunAsync(a, ["regenerate-codes"], gateWait: TimeSpan.FromMilliseconds(500));

        run.Exit.Should().Be(EmergencyExitCodes.Retryable);
        run.Console.ShownCodes().Should().BeEmpty();
        (await ActiveAsync(a)).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Status_reads_the_posture_through_the_services_and_exits_zero(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);

        var run = await RunAsync(a, ["status"]);

        run.Exit.Should().Be(EmergencyExitCodes.Success, run.Console.Complaints);
        run.Console.Shown.Should().Contain("Authenticator: Enrolled").And.Contain($"Designated user id: {a.Id}");
    }

    // ================================================================================================ the production container

    private sealed class FixedProfile(string connectionString) : Apperture.Bootstrap.IBootstrapConnectionProfile
    {
        public string ConnectionString => connectionString;

        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Runs <c>EmergencyCli.RunAsync</c> as the process does: the production composition, pointed at the catalog, with the test protector the arrangement used.</summary>
    private static async Task<CliRun> RunThroughTheProductionContainerAsync(Account a, string[] line, Func<OperatorConsole, int, Task<string?>>? type)
    {
        var console = new OperatorConsole(redirected: false, type);
        var exit = await EmergencyCli.RunAsync(
            line,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            "Production",
            console,
            services =>
            {
                services.AddSingleton<Apperture.Bootstrap.IBootstrapConnectionProfile>(new FixedProfile(a.W.Scratch.ConnectionString));
                services.AddSingleton<Apperture.Core.Security.Secrets.Abstractions.ISecretProtector>(a.W.Protector);
            });
        return new CliRun(exit, console);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_production_container_runs_the_whole_replacement_through_the_application_services(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        // The container has the real clock: step the account's last used authenticator step back so a code for now is a new step.
        await a.W.Scratch.ExecuteAsync($"UPDATE UserTotpCredentials SET LastUsedStep = LastUsedStep - 10 WHERE UserId = N'{a.Id}';");

        var run = await RunThroughTheProductionContainerAsync(
            a,
            ["reset-mfa", "--user-id", a.Id],
            (console, _) => Task.FromResult<string?>(Rfc6238Totp.Compute(console.ShownKey(), DateTimeOffset.UtcNow.ToUnixTimeSeconds())));

        run.Exit.Should().Be(EmergencyExitCodes.Success, run.Console.Complaints);
        run.Console.ShownCodes().Should().HaveCount(10);
        (await a.W.RowsAsync(AuditActionNames.MfaReplaced, a.Id)).Should().ContainSingle();
        (await a.W.RowsAsync(AuditActionNames.MfaReset, a.Id)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_production_container_reports_the_bare_reset_evidence_gap_with_its_own_exit_status(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi, fallback: "local");
        var before = await ActiveAsync(a);

        var run = await RunThroughTheProductionContainerAsync(a, ["reset-mfa", "--bare-reset", "--user-id", a.Id], type: null);

        run.Exit.Should().Be(EmergencyExitCodes.EvidenceUnavailable, run.Console.Complaints);
        (await ActiveAsync(a)).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_production_container_reads_the_posture_for_status(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);

        var run = await RunThroughTheProductionContainerAsync(a, ["status"], type: null);

        run.Exit.Should().Be(EmergencyExitCodes.Success, run.Console.Complaints);
        run.Console.Shown.Should().Contain("Authenticator: Enrolled");
    }
}
