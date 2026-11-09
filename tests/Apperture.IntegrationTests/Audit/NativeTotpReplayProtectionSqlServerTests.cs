using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.MfaInterleavingSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// A TOTP code admits at most one sign-in, and only with the authenticator it was checked against, against real SQL Server on both
/// isolation modes. A sign-in reads the credential without a lock and checks the code; recording the matched step is one
/// conditional statement (enrolled, same credential identity, strictly newer step) and the sign-in fails when it changes nothing.
/// The interleavings are exact: a decorator of the real repository runs the competing writer between the sign-in's read and its
/// write, or a competing write is held uncommitted until the server reports the sign-in waiting for it. Regression cases of the
/// independent review's finding B-1 (probes F2a, F2b and F2c). Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class NativeTotpReplayProtectionSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public NativeTotpReplayProtectionSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ---- A switch between a sign-in's read and its write (F2a, F2b) ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_sign_in_that_read_the_replaced_credential_cannot_lower_the_step_the_switch_recorded(bool rcsi)
    {
        // F2a. The switch records step S (the step the candidate's verification code matched); the stale sign-in then presents the
        // replaced credential and its own lower step. Before the correction the stored step became S-1.
        var world = await WorldAsync(rcsi);
        var s = MfaCandidateWorld.CurrentStep();
        await SetLastUsedStepAsync(world, s - 5);
        var replaced = await world.ActiveCredentialIdAsync();
        var switched = await SwitchToVerifiedCandidateAsync(world, verifiedStep: s);

        bool recorded;
        await using (var db = world.Scratch.NewDb())
            recorded = await new SqlNativeTotpCredentialRepository(db).UpdateLastUsedAsync(world.UserId, replaced, s - 1, DateTimeOffset.UtcNow);

        Record($"F2a rcsi={rcsi}: S={s}; stale write (replaced credential, S-1) recorded={recorded}; step now {await LastUsedStepAsync(world)}");
        recorded.Should().BeFalse("the credential the sign-in checked is no longer the active one");
        (await LastUsedStepAsync(world)).Should().Be(s, "the step the candidate's verification code consumed stays consumed");
        (await world.ActiveCredentialIdAsync()).Should().Be(switched);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_sign_in_that_read_the_replaced_credential_cannot_record_even_a_newer_step_on_the_new_one(bool rcsi)
    {
        // The identity condition on its own: the stale step is above the switched one, so the monotonic condition would let it through.
        var world = await WorldAsync(rcsi);
        var s = MfaCandidateWorld.CurrentStep();
        await SetLastUsedStepAsync(world, s - 5);
        var replaced = await world.ActiveCredentialIdAsync();
        await SwitchToVerifiedCandidateAsync(world, verifiedStep: s);
        var before = await world.ActiveAsync();

        bool recorded;
        await using (var db = world.Scratch.NewDb())
            recorded = await new SqlNativeTotpCredentialRepository(db).UpdateLastUsedAsync(world.UserId, replaced, s + 1, DateTimeOffset.UtcNow);

        recorded.Should().BeFalse("a step of the replaced secret is never recorded on its replacement");
        (await world.ActiveAsync()).Should().Be(before, "nothing about the new credential changed");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_sign_in_with_the_retired_authenticator_does_not_complete_after_the_switch_and_the_candidate_code_is_not_accepted_again(bool rcsi)
    {
        // F2b, end to end through the real service. Secret A is active (last step S-5); candidate B was verified with B's code for
        // step S. A sign-in checks A's code for step S-1 against the row it read; the switch commits before it writes. Before the
        // correction the stale sign-in returned true, lowered the step to S-1, and B's step-S code was then accepted a second time.
        var world = await WorldAsync(rcsi, enrolled: false);
        var secretA = await EnrollWithKnownSecretAsync(world);
        var s = MfaCandidateWorld.CurrentStep();
        await SetLastUsedStepAsync(world, s - 5);
        var (secretB, protectedB) = await world.NewSecretAsync();
        var candidate = await world.StageAsync(protectedSecret: protectedB);
        var verified = await world.VerifyAsync(candidate.CandidateId, candidate.Version, step: s);
        verified.IsApplied.Should().BeTrue("precondition: B's code for step S verified the candidate");
        var time = new FixedTime(DateTimeOffset.FromUnixTimeSeconds(s * 30 + 10));

        NativeMfaSwitchResult? switchedBetween = null;
        bool staleSignIn;
        await using (var db = world.Scratch.NewDb())
        {
            var repository = new InterleavingTotpRepository(
                new SqlNativeTotpCredentialRepository(db),
                afterFirstRead: async () => switchedBetween = await world.SwitchAsync(world.Request(candidate.CandidateId, verified.Version!.Value)));
            staleSignIn = await Service(repository, db, time).VerifyTotpAsync(world.UserId, Rfc6238Totp.Compute(secretA, (s - 1) * 30));
        }

        switchedBetween.Should().NotBeNull("the decorator ran the switch between the sign-in's read and its write");
        switchedBetween!.IsApplied.Should().BeTrue("precondition: the switch applied");
        var stepAfterStale = await LastUsedStepAsync(world);

        bool replay;
        await using (var db = world.Scratch.NewDb())
            replay = await Service(new SqlNativeTotpCredentialRepository(db), db, time).VerifyTotpAsync(world.UserId, Rfc6238Totp.Compute(secretB, s * 30));

        Record($"F2b rcsi={rcsi}: S={s}; stale sign-in returned {staleSignIn}; step after it {stepAfterStale}; replay returned {replay}; step now {await LastUsedStepAsync(world)}");
        staleSignIn.Should().BeFalse("a code of the retired authenticator does not complete a sign-in once the replacement committed");
        stepAfterStale.Should().Be(s, "the stale sign-in recorded nothing");
        replay.Should().BeFalse("the code that verified the candidate was consumed by the switch");
        (await LastUsedStepAsync(world)).Should().Be(s);
        (await world.ActiveCredentialIdAsync()).Should().Be(switchedBetween.NewCredentialId);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_sign_in_with_the_retired_authenticator_at_a_step_newer_than_the_switch_does_not_complete_and_the_new_one_still_signs_in(bool rcsi)
    {
        // The identity condition through the service: the candidate was verified at step S-1, the stale sign-in's code matches step S
        // of the old secret, so only the identity refuses it. The new authenticator's own step-S code is then a first use and succeeds.
        var world = await WorldAsync(rcsi, enrolled: false);
        var secretA = await EnrollWithKnownSecretAsync(world);
        var s = MfaCandidateWorld.CurrentStep();
        await SetLastUsedStepAsync(world, s - 5);
        var (secretB, protectedB) = await world.NewSecretAsync();
        var candidate = await world.StageAsync(protectedSecret: protectedB);
        var verified = await world.VerifyAsync(candidate.CandidateId, candidate.Version, step: s - 1);
        var time = new FixedTime(DateTimeOffset.FromUnixTimeSeconds(s * 30 + 10));

        NativeMfaSwitchResult? switchedBetween = null;
        bool staleSignIn;
        await using (var db = world.Scratch.NewDb())
        {
            var repository = new InterleavingTotpRepository(
                new SqlNativeTotpCredentialRepository(db),
                afterFirstRead: async () => switchedBetween = await world.SwitchAsync(world.Request(candidate.CandidateId, verified.Version!.Value)));
            staleSignIn = await Service(repository, db, time).VerifyTotpAsync(world.UserId, Rfc6238Totp.Compute(secretA, s * 30));
        }

        switchedBetween!.IsApplied.Should().BeTrue("precondition: the switch applied");
        staleSignIn.Should().BeFalse("the retired authenticator's code does not complete a sign-in, whatever its step");
        (await LastUsedStepAsync(world)).Should().Be(s - 1, "the stale sign-in recorded nothing on the new credential");

        bool firstUseOfNew;
        await using (var db = world.Scratch.NewDb())
            firstUseOfNew = await Service(new SqlNativeTotpCredentialRepository(db), db, time).VerifyTotpAsync(world.UserId, Rfc6238Totp.Compute(secretB, s * 30));
        firstUseOfNew.Should().BeTrue("the new authenticator's code for a step it has not used signs in");
        (await LastUsedStepAsync(world)).Should().Be(s);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task After_a_switch_the_code_that_verified_the_candidate_is_not_accepted_as_a_sign_in(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var s = MfaCandidateWorld.CurrentStep();
        var (secretB, protectedB) = await world.NewSecretAsync();
        var candidate = await world.StageAsync(protectedSecret: protectedB);
        var verified = await world.VerifyAsync(candidate.CandidateId, candidate.Version, step: s);
        (await world.SwitchAsync(world.Request(candidate.CandidateId, verified.Version!.Value))).IsApplied.Should().BeTrue();
        var time = new FixedTime(DateTimeOffset.FromUnixTimeSeconds(s * 30 + 10));

        await using var db = world.Scratch.NewDb();
        (await Service(new SqlNativeTotpCredentialRepository(db), db, time).VerifyTotpAsync(world.UserId, Rfc6238Totp.Compute(secretB, s * 30)))
            .Should().BeFalse("the switch recorded the step of the candidate's verification code as used");
        (await LastUsedStepAsync(world)).Should().Be(s);
    }

    // ---- Two sign-ins with one code (F2c) ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Of_two_sign_ins_with_one_code_that_both_read_the_credential_before_either_wrote_exactly_one_succeeds(bool rcsi)
    {
        // F2c, the stale-read order: caller 1 reads, caller 2 reads, caller 1 writes and finishes, caller 2 writes. Before the
        // correction both returned true.
        var world = await WorldAsync(rcsi, enrolled: false);
        var secret = await EnrollWithKnownSecretAsync(world);
        var s = MfaCandidateWorld.CurrentStep();
        await SetLastUsedStepAsync(world, s - 5);
        var time = new FixedTime(DateTimeOffset.FromUnixTimeSeconds(s * 30 + 10));
        var code = Rfc6238Totp.Compute(secret, s * 30);

        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var caller1 = Task.Run(async () =>
        {
            try
            {
                await using var db = world.Scratch.NewDb();
                var repository = new InterleavingTotpRepository(
                    new SqlNativeTotpCredentialRepository(db),
                    afterFirstRead: async () =>
                    {
                        firstRead.TrySetResult();
                        await secondRead.Task.WaitAsync(Patience);
                    });
                return await Service(repository, db, time).VerifyTotpAsync(world.UserId, code);
            }
            finally
            {
                firstDone.TrySetResult();
            }
        });

        var caller2 = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb();
            var repository = new InterleavingTotpRepository(
                new SqlNativeTotpCredentialRepository(db),
                afterFirstRead: async () =>
                {
                    secondRead.TrySetResult();
                    await firstRead.Task.WaitAsync(Patience);
                },
                beforeUpdate: () => firstDone.Task.WaitAsync(Patience));
            return await Service(repository, db, time).VerifyTotpAsync(world.UserId, code);
        });

        var first = await caller1.WaitAsync(Patience);
        var second = await caller2.WaitAsync(Patience);
        Record($"F2c rcsi={rcsi}: S={s}; caller1={first} caller2={second}; step now {await LastUsedStepAsync(world)}");

        first.Should().BeTrue("the first sign-in to record the step signs in");
        second.Should().BeFalse("the code's step was consumed by the first sign-in");
        (await LastUsedStepAsync(world)).Should().Be(s);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_sign_in_that_waits_for_another_sign_in_recording_the_same_step_fails_once_that_one_commits(bool rcsi)
    {
        // F2c, the locking order: the first sign-in's step write is held uncommitted; the second sign-in runs until the server reports
        // it waiting for that row (its read under locking read committed, its write under read committed snapshot), then the first
        // commits. The second must decide against the committed step, not against what it read before.
        var world = await WorldAsync(rcsi, enrolled: false);
        var secret = await EnrollWithKnownSecretAsync(world);
        var s = MfaCandidateWorld.CurrentStep();
        await SetLastUsedStepAsync(world, s - 5);
        var credential = await world.ActiveCredentialIdAsync();
        var time = new FixedTime(DateTimeOffset.FromUnixTimeSeconds(s * 30 + 10));
        var log = new RoleAuthorityWorld.DeadlockLog();

        await using var holderDb = Db(world.Scratch, ParkedApplication, retry: false, log);
        await using var holding = await holderDb.Database.BeginTransactionAsync();
        (await new SqlNativeTotpCredentialRepository(holderDb).UpdateLastUsedAsync(world.UserId, credential, s, DateTimeOffset.UtcNow))
            .Should().BeTrue("precondition: the first sign-in recorded step S, not yet committed");

        var second = Task.Run(async () =>
        {
            await using var db = Db(world.Scratch, OtherApplication, retry: false, log);
            return await Service(new SqlNativeTotpCredentialRepository(db), db, time).VerifyTotpAsync(world.UserId, Rfc6238Totp.Compute(secret, s * 30));
        });

        var state = await WaitUntilWaitingOrFinishedAsync(world.Scratch, OtherApplication, second);
        state.Should().Be(OtherState.WaitingOnLock, "the second sign-in reaches the row the first one holds");
        await holding.CommitAsync();

        var result = await second.WaitAsync(Patience);
        Record($"F2c-held rcsi={rcsi}: S={s}; second sign-in returned {result}; step now {await LastUsedStepAsync(world)}");
        result.Should().BeFalse("the step was consumed by the sign-in that committed first");
        (await LastUsedStepAsync(world)).Should().Be(s);
    }

    // ---- The conditions of the step write, one at a time ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_step_at_or_below_the_recorded_step_is_refused_and_changes_nothing(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var s = MfaCandidateWorld.CurrentStep();
        await SetLastUsedStepAsync(world, s);
        var credential = await world.ActiveCredentialIdAsync();
        var before = await world.ActiveAsync();

        await using var db = world.Scratch.NewDb();
        var repository = new SqlNativeTotpCredentialRepository(db);
        (await repository.UpdateLastUsedAsync(world.UserId, credential, s, DateTimeOffset.UtcNow)).Should().BeFalse("the same step was consumed");
        (await repository.UpdateLastUsedAsync(world.UserId, credential, s - 1, DateTimeOffset.UtcNow)).Should().BeFalse("an earlier step was consumed");

        (await world.ActiveAsync()).Should().Be(before, "a refused write changes neither the step nor the verification time");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_newer_step_of_the_enrolled_credential_signs_in_and_is_recorded(bool rcsi)
    {
        var world = await WorldAsync(rcsi, enrolled: false);
        var secret = await EnrollWithKnownSecretAsync(world);
        var s = MfaCandidateWorld.CurrentStep();
        await SetLastUsedStepAsync(world, s - 5);
        var time = new FixedTime(DateTimeOffset.FromUnixTimeSeconds(s * 30 + 10));

        await using (var db = world.Scratch.NewDb())
            (await Service(new SqlNativeTotpCredentialRepository(db), db, time).VerifyTotpAsync(world.UserId, Rfc6238Totp.Compute(secret, s * 30)))
                .Should().BeTrue("a valid code for a step not yet used signs in");
        (await LastUsedStepAsync(world)).Should().Be(s);

        var credential = await world.ActiveCredentialIdAsync();
        var at = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        await using (var db = world.Scratch.NewDb())
            (await new SqlNativeTotpCredentialRepository(db).UpdateLastUsedAsync(world.UserId, credential, s + 1, at)).Should().BeTrue();
        (await LastUsedStepAsync(world)).Should().Be(s + 1);
        (await world.Scratch.ScalarAsync<DateTimeOffset>($"SELECT LastVerifiedAtUtc FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';"))
            .Should().Be(at);
        (await world.ActiveCredentialIdAsync()).Should().Be(credential, "signing in never changes the identity");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_pending_credential_never_records_a_sign_in_step(bool rcsi)
    {
        // The enrolled condition on its own: the pending row's own identity is presented, so only the enrolled state refuses it.
        var world = await WorldAsync(rcsi, enrolled: false);
        await using (var db = world.Scratch.NewDb())
            await world.Mfa(db).BeginEnrollmentAsync(world.UserId, "pending");
        var pending = await world.ActiveCredentialIdAsync();
        var before = await world.ActiveAsync();

        await using (var db = world.Scratch.NewDb())
            (await new SqlNativeTotpCredentialRepository(db).UpdateLastUsedAsync(world.UserId, pending, MfaCandidateWorld.CurrentStep(), DateTimeOffset.UtcNow))
                .Should().BeFalse("a pending authenticator has not been enrolled, so no code of it signs in");

        (await world.ActiveAsync()).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_step_write_changes_only_its_own_user_even_where_credential_identities_coincide(bool rcsi)
    {
        // The owner condition on its own: two users are given the same credential identity, so only the user id tells them apart.
        var world = await WorldAsync(rcsi);
        var other = await world.OtherEnrolledUserAsync();
        var shared = await world.ActiveCredentialIdAsync();
        await world.Scratch.ExecuteAsync($"UPDATE UserTotpCredentials SET CredentialId = N'{shared}', LastUsedStep = 1 WHERE UserId IN (N'{world.UserId.Value}', N'{other.Value}');");
        var otherBefore = await world.ActiveAsync(other);
        var s = MfaCandidateWorld.CurrentStep();

        await using (var db = world.Scratch.NewDb())
            (await new SqlNativeTotpCredentialRepository(db).UpdateLastUsedAsync(world.UserId, shared, s, DateTimeOffset.UtcNow))
                .Should().BeTrue();

        (await LastUsedStepAsync(world)).Should().Be(s);
        (await world.ActiveAsync(other)).Should().Be(otherBefore, "the other user's credential is not touched");
    }

    // ---- Arrangement ----

    private async Task<MfaCandidateWorld> WorldAsync(bool rcsi, bool enrolled = true)
    {
        var world = await MfaCandidateWorld.CreateAsync(_catalogs, rcsi, enrolled);
        Record(await ServerAsync(world.Scratch, rcsi));
        return world;
    }

    /// <summary>Stages a candidate, verifies it at <paramref name="verifiedStep"/> and switches to it; returns the new credential identity.</summary>
    private static async Task<string> SwitchToVerifiedCandidateAsync(MfaCandidateWorld world, long verifiedStep)
    {
        var candidate = await world.StageAsync();
        var verified = await world.VerifyAsync(candidate.CandidateId, candidate.Version, step: verifiedStep);
        verified.IsApplied.Should().BeTrue("precondition: the candidate verification applied");
        var switched = await world.SwitchAsync(world.Request(candidate.CandidateId, verified.Version!.Value));
        switched.IsApplied.Should().BeTrue("precondition: the switch applied");
        (await LastUsedStepAsync(world)).Should().Be(verifiedStep, "precondition: the switch recorded the candidate's matched step");
        return switched.NewCredentialId!;
    }

    /// <summary>Enrolls the world's user through the real service and returns the plain secret, as an authenticator app holds it.</summary>
    private static async Task<byte[]> EnrollWithKnownSecretAsync(MfaCandidateWorld world)
    {
        await using var db = world.Scratch.NewDb();
        var mfa = world.Mfa(db);
        var challenge = await mfa.BeginEnrollmentAsync(world.UserId, "replay");
        var secret = Base32.Decode(challenge.ManualEntryKey);
        await mfa.CompleteEnrollmentAsync(world.UserId, Rfc6238Totp.Compute(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), AuditActor.EmergencyCli);
        return secret;
    }

    private static NativeMfaService Service(INativeTotpCredentialRepository totp, AppertureDbContext db, TimeProvider time) =>
        new(
            totp,
            new SqlNativeMfaRecoveryCodeRepository(db),
            new LocalPasswordHasher(),
            new ManagementHost.IntegrationTestSecretProtector(),
            time,
            NullLogger<NativeMfaService>.Instance,
            AuditRecorderTestSupport.NewUnit(db, null));

    private static Task<long?> LastUsedStepAsync(MfaCandidateWorld world) =>
        world.Scratch.ScalarAsync<long?>($"SELECT LastUsedStep FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';");

    private static Task SetLastUsedStepAsync(MfaCandidateWorld world, long step) =>
        world.Scratch.ExecuteAsync($"UPDATE UserTotpCredentials SET LastUsedStep = {step} WHERE UserId = N'{world.UserId.Value}';");

    private void Record(string line) => _output.WriteLine("REPLAY " + line);

    /// <summary>The real repository, with a hook after the first read and before the step write: the exact interleaving point.</summary>
    private sealed class InterleavingTotpRepository(
        INativeTotpCredentialRepository inner,
        Func<Task>? afterFirstRead = null,
        Func<Task>? beforeUpdate = null) : INativeTotpCredentialRepository
    {
        private int _reads;

        public async ValueTask<NativeTotpStoredCredential?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            var stored = await inner.GetByUserIdAsync(userId, cancellationToken);
            if (afterFirstRead is not null && Interlocked.Increment(ref _reads) == 1)
                await afterFirstRead();
            return stored;
        }

        public ValueTask<bool> UpsertPendingAsync(UserId userId, byte[] protectedSecret, string protectorKeyId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default) =>
            inner.UpsertPendingAsync(userId, protectedSecret, protectorKeyId, startedAtUtc, cancellationToken);

        public ValueTask<bool> MarkEnrolledAsync(UserId userId, string expectedCredentialId, long lastUsedStep, DateTimeOffset enrolledAtUtc, CancellationToken cancellationToken = default) =>
            inner.MarkEnrolledAsync(userId, expectedCredentialId, lastUsedStep, enrolledAtUtc, cancellationToken);

        public async ValueTask<bool> UpdateLastUsedAsync(UserId userId, string expectedCredentialId, long matchedStep, DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken = default)
        {
            if (beforeUpdate is not null)
                await beforeUpdate();
            return await inner.UpdateLastUsedAsync(userId, expectedCredentialId, matchedStep, verifiedAtUtc, cancellationToken);
        }

        public ValueTask DeleteAsync(UserId userId, CancellationToken cancellationToken = default) => inner.DeleteAsync(userId, cancellationToken);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
