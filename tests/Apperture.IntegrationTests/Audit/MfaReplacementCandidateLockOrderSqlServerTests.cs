using System.Data;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.MfaInterleavingSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The lock order of the replacement candidates (credential row, then candidate rows, then recovery codes) holds between staging or
/// switching and every writer that removes candidates, for the same user and across users, against real SQL Server on both
/// isolation modes. One operation is parked with its locks; the other runs until it finishes or the server reports it waiting for a
/// lock; then the first is released. Each case runs once on contexts without any retry, so a deadlock victim surfaces as SQL error
/// 1205 to the test, and once with the production retry, which counts the deadlock victims it retried; both must be zero. The end
/// state must be one of the serial outcomes. Regression cases of the independent review's finding R-1 (probes F1a, F1b and F1c):
/// before the correction, a staging and a same-user candidate deleter, and a staging and another user's deleter, deadlocked.
/// Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class MfaReplacementCandidateLockOrderSqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public MfaReplacementCandidateLockOrderSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public void Dispose() => RoleAuthorityWorld.PauseAt.ReleaseAll();

    public enum Deleter
    {
        /// <summary>NativeMfaService.RegenerateRecoveryCodesAsync (audited unit; the code replacement removes candidates first).</summary>
        RegenerateRecoveryCodes,

        /// <summary>NativeMfaService.ResetAsync (audited unit; the credential, then candidates, then the codes).</summary>
        ResetMfa,

        /// <summary>SqlNativeMfaRecoveryCodeRepository.DeleteAllAsync, in a transaction of its own.</summary>
        DeleteAllRecoveryCodes,

        /// <summary>SqlNativeMfaReplacementCandidateRepository.DiscardAsync, in a transaction of its own.</summary>
        DiscardCandidates,
    }

    public enum KeyOrder
    {
        /// <summary>The live candidate's id sorts before the history row's: the order that deadlocked before the correction.</summary>
        LiveSortsFirst,

        /// <summary>The history row's id sorts before the live candidate's.</summary>
        HistorySortsFirst,
    }

    public static IEnumerable<object[]> SameUserCases()
    {
        foreach (var rcsi in new[] { false, true })
        foreach (var deleter in Enum.GetValues<Deleter>())
        foreach (var order in Enum.GetValues<KeyOrder>())
        foreach (var retry in new[] { false, true })
            yield return new object[] { rcsi, deleter, order, retry };
    }

    public static IEnumerable<object[]> CrossUserCases()
    {
        foreach (var rcsi in new[] { false, true })
        foreach (var deleter in Enum.GetValues<Deleter>())
        foreach (var retry in new[] { false, true })
            yield return new object[] { rcsi, deleter, retry };
    }

    // ---- F1a: staging against a same-user deleter ----

    [Theory]
    [MemberData(nameof(SameUserCases))]
    public async Task A_staging_and_a_candidate_deleter_of_the_same_user_run_one_after_the_other_without_deadlock(bool rcsi, Deleter deleter, KeyOrder order, bool retry)
    {
        // The staging is parked right after its first statement (the history purge), holding the credential row and the history row.
        // The user also has a live candidate.
        var world = await WorldAsync(rcsi);
        var user = world.UserId;
        var credential = await world.ActiveCredentialIdAsync();
        var (low, high) = Keys();
        var (liveId, historyId) = order == KeyOrder.LiveSortsFirst ? (low, high) : (high, low);
        await SeedCandidateAsync(world.Scratch, historyId, user, status: 3, version: 3, secret: [], stagedAgainst: credential, matchedStep: 1);
        await SeedCandidateAsync(world.Scratch, liveId, user, status: 0, version: 1, secret: (await world.NewSecretAsync()).Protected, stagedAgainst: credential, matchedStep: null);
        var before = await world.ActiveAsync();
        var codesBefore = await world.CodeIdsAsync();
        var stageSecret = (await world.NewSecretAsync()).Protected;
        var stageHash = MfaCandidateWorld.HashOf(stageSecret);

        var pause = AfterStageHistoryPurge();
        var stageLog = new RoleAuthorityWorld.DeadlockLog();
        var deleterLog = new RoleAuthorityWorld.DeadlockLog();
        var stage = RunSide("stage", stageLog, async () =>
        {
            await using var db = Db(world.Scratch, ParkedApplication, retry, stageLog, pause);
            var result = await new SqlNativeMfaReplacementCandidateRepository(db)
                .StageAsync(user, stageSecret, NativeMfaService.TotpProtectorKeyId, TimeSpan.FromMinutes(10));
            return (result.IsApplied, result.Outcome.ToString());
        });

        var run = await InterleaveAsync(world.Scratch, pause, stage, () => RunSide(deleter.ToString(), deleterLog, async () =>
        {
            await using var db = Db(world.Scratch, OtherApplication, retry, deleterLog);
            return await RunDeleterAsync(deleter, db, user);
        }));

        var rows = await CandidatesAsync(world.Scratch, user);
        var codes = await world.CodeIdsAsync();
        var credentialRows = await CredentialCountAsync(world.Scratch, user);
        var credentialNow = (await world.ActiveAsync()).Credential;
        Record($"F1a rcsi={rcsi} deleter={deleter} order={order} retry={retry}: live={liveId} history={historyId}");
        RecordRun(run);
        Record($"  post-state: credentialRows={credentialRows} credentialUnchanged={credentialNow == before.Credential} " +
               $"codesUnchanged={codes == codesBefore} candidates=[{string.Join("; ", rows)}]");

        var staging = run.Parked;
        var deleting = run.Other;
        (staging.Deadlocked || deleting.Deadlocked).Should().BeFalse(
            $"the staging and the deleter take the user's credential row first, so they cannot form a lock cycle (stage: {staging}; deleter: {deleting})");
        staging.Succeeded.Should().BeTrue($"the staging completes ({staging})");
        run.OtherWhenReleased.Should().Be(OtherState.WaitingOnLock, "the deleter waits for the user's credential row the staging holds");

        if (deleter == Deleter.ResetMfa)
        {
            // The reset ran second: it removed the credential, every candidate (the staged one included) and the codes.
            deleting.Succeeded.Should().BeTrue($"the reset completes after the staging ({deleting})");
            credentialRows.Should().Be(0);
            codes.Should().BeNull();
            rows.Should().BeEmpty();
            return;
        }

        deleting.Succeeded.Should().BeTrue($"the deleter completes after the staging ({deleting})");
        credentialNow.Should().Be(before.Credential, "neither side changes the active credential");
        rows.Should().BeEmpty("the deleter ran second and removed every candidate, the staged one included");
        rows.Should().NotContain(r => r.SecretHash == stageHash);
        switch (deleter)
        {
            case Deleter.RegenerateRecoveryCodes:
                codes.Should().NotBeNull().And.NotBe(codesBefore, "the regeneration replaced the codes");
                break;
            case Deleter.DeleteAllRecoveryCodes:
                codes.Should().BeNull("the codes were deleted");
                break;
            case Deleter.DiscardCandidates:
                codes.Should().Be(codesBefore, "a discard leaves the codes");
                break;
        }
    }

    // ---- F1b: staging for one user against another user's deleter ----

    [Theory]
    [MemberData(nameof(CrossUserCases))]
    public async Task A_staging_never_waits_for_or_deadlocks_with_another_users_candidate_deleter(bool rcsi, Deleter deleter, bool retry)
    {
        // The staging for user 1 is parked after its history purge, holding user 1's history row (highest key). User 2's rows have
        // the lowest keys. Before the correction user 2's delete scanned the clustered index (the only index on the owner was
        // filtered), waited for user 1's row, and the resumed staging waited for user 2's rows: a cross-user cycle.
        var world = await WorldAsync(rcsi);
        var user1 = world.UserId;
        var user2 = await world.OtherEnrolledUserAsync();
        var credential1 = await world.ActiveCredentialIdAsync(user1);
        var credential2 = await world.ActiveCredentialIdAsync(user2);
        var tag = Guid.NewGuid().ToString("N")[..24];
        var live2 = "00000000" + tag;
        var history2 = "00000001" + tag;
        var live1 = "77777777" + tag;
        var history1 = "ffffffff" + tag;
        await SeedCandidateAsync(world.Scratch, live2, user2, 0, 1, (await world.NewSecretAsync()).Protected, credential2, null);
        await SeedCandidateAsync(world.Scratch, history2, user2, 3, 3, [], credential2, 1);
        await SeedCandidateAsync(world.Scratch, live1, user1, 0, 1, (await world.NewSecretAsync()).Protected, credential1, null);
        await SeedCandidateAsync(world.Scratch, history1, user1, 3, 3, [], credential1, 1);
        var codes1Before = await world.CodeIdsAsync(user1);
        var codes2Before = await world.CodeIdsAsync(user2);
        var stageSecret = (await world.NewSecretAsync()).Protected;
        var stageHash = MfaCandidateWorld.HashOf(stageSecret);

        var pause = AfterStageHistoryPurge();
        var stageLog = new RoleAuthorityWorld.DeadlockLog();
        var deleterLog = new RoleAuthorityWorld.DeadlockLog();
        var stage = RunSide("stage(user1)", stageLog, async () =>
        {
            await using var db = Db(world.Scratch, ParkedApplication, retry, stageLog, pause);
            var result = await new SqlNativeMfaReplacementCandidateRepository(db)
                .StageAsync(user1, stageSecret, NativeMfaService.TotpProtectorKeyId, TimeSpan.FromMinutes(10));
            return (result.IsApplied, result.Outcome.ToString());
        });

        var run = await InterleaveAsync(world.Scratch, pause, stage, () => RunSide($"{deleter}(user2)", deleterLog, async () =>
        {
            await using var db = Db(world.Scratch, OtherApplication, retry, deleterLog);
            return await RunDeleterAsync(deleter, db, user2);
        }));

        var rows1 = await CandidatesAsync(world.Scratch, user1);
        var rows2 = await CandidatesAsync(world.Scratch, user2);
        var codes1 = await world.CodeIdsAsync(user1);
        var codes2 = await world.CodeIdsAsync(user2);
        Record($"F1b rcsi={rcsi} deleter={deleter} retry={retry}: user1 live={live1} history={history1}; user2 live={live2} history={history2}");
        RecordRun(run);
        Record($"  post-state user1: codesUnchanged={codes1 == codes1Before} candidates=[{string.Join("; ", rows1)}]");
        Record($"  post-state user2: codesUnchanged={codes2 == codes2Before} candidates=[{string.Join("; ", rows2)}]");

        var staging = run.Parked;
        var deleting = run.Other;
        (staging.Deadlocked || deleting.Deadlocked).Should().BeFalse(
            $"one user's staging and another user's deleter share no row (stage: {staging}; deleter: {deleting})");
        run.OtherWhenReleased.Should().Be(OtherState.Finished,
            $"user 2's deleter seeks its own rows and never waits for user 1's (requests while parked: {run.RequestsWhileParked})");
        staging.Succeeded.Should().BeTrue($"the staging completes ({staging})");
        deleting.Succeeded.Should().BeTrue($"the deleter completes ({deleting})");

        codes1.Should().Be(codes1Before, "nothing touched user 1's codes");
        rows1.Should().HaveCount(2, "user 1's history purged, live superseded, new staged");
        rows1.Single(r => r.Id == live1).Status.Should().Be(2);
        rows1.Single(r => r.Id == live1).SecretBytes.Should().Be(0);
        rows1.Single(r => r.SecretHash == stageHash).Status.Should().Be(0);

        rows2.Should().BeEmpty("user 2's candidates were removed");
        switch (deleter)
        {
            case Deleter.RegenerateRecoveryCodes:
                codes2.Should().NotBeNull().And.NotBe(codes2Before);
                break;
            case Deleter.ResetMfa:
            case Deleter.DeleteAllRecoveryCodes:
                codes2.Should().BeNull();
                break;
            case Deleter.DiscardCandidates:
                codes2.Should().Be(codes2Before);
                break;
        }
    }

    // ---- F1c: a switch against a same-user deleter ----

    [Theory]
    [MemberData(nameof(SameUserCases))]
    public async Task A_switch_and_a_candidate_deleter_of_the_same_user_run_one_after_the_other_without_deadlock(bool rcsi, Deleter deleter, KeyOrder order, bool retry)
    {
        // The switch is parked right after its conditional update (the credential row written, the candidate row locked), with a
        // history row of the same user sorting before or after the switching candidate.
        var world = await WorldAsync(rcsi);
        var user = world.UserId;
        var credential = await world.ActiveCredentialIdAsync();
        var (_, protectedB) = await world.NewSecretAsync();
        var (candidate, version) = await world.StageAndVerifyAsync(protectedB);
        var (low, high) = Keys();
        var historyId = order == KeyOrder.HistorySortsFirst ? low : high;
        await SeedCandidateAsync(world.Scratch, historyId, user, 3, 3, [], credential, 1);
        var before = await world.ActiveAsync();
        var codesBefore = await world.CodeIdsAsync();
        var request = world.Request(candidate.CandidateId, version);
        var switchCodes = string.Join(",", request.RecoveryCodes.Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal));

        var pause = new RoleAuthorityWorld.PauseAt(
            c => c.CommandText.Contains("INNER JOIN [UserTotpReplacementCandidates] AS c WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal),
            after: true);
        var switchLog = new RoleAuthorityWorld.DeadlockLog();
        var deleterLog = new RoleAuthorityWorld.DeadlockLog();
        string? newCredentialId = null;
        var switching = RunSide("switch", switchLog, async () =>
        {
            await using var db = Db(world.Scratch, ParkedApplication, retry, switchLog, pause);
            var result = await new SqlNativeMfaReplacementCandidateRepository(db).SwitchAsync(request);
            newCredentialId = result.NewCredentialId;
            return (result.IsApplied, result.Outcome.ToString());
        });

        var run = await InterleaveAsync(world.Scratch, pause, switching, () => RunSide(deleter.ToString(), deleterLog, async () =>
        {
            await using var db = Db(world.Scratch, OtherApplication, retry, deleterLog);
            return await RunDeleterAsync(deleter, db, user);
        }));

        var rows = await CandidatesAsync(world.Scratch, user);
        var codes = await world.CodeIdsAsync();
        var credentialRows = await CredentialCountAsync(world.Scratch, user);
        Record($"F1c rcsi={rcsi} deleter={deleter} order={order} retry={retry}: switching candidate={candidate.CandidateId} history={historyId}");
        RecordRun(run);
        Record($"  post-state: credentialRows={credentialRows} codes={(codes == switchCodes ? "switch's" : codes == codesBefore ? "original" : codes is null ? "none" : "other")} " +
               $"candidates=[{string.Join("; ", rows)}]");

        var sw = run.Parked;
        var deleting = run.Other;
        (sw.Deadlocked || deleting.Deadlocked).Should().BeFalse(
            $"the switch and the deleter take the user's credential row first, so they cannot form a lock cycle (switch: {sw}; deleter: {deleting})");
        sw.Succeeded.Should().BeTrue($"the switch completes ({sw})");
        run.OtherWhenReleased.Should().Be(OtherState.WaitingOnLock, "the deleter waits for the credential row the switch holds");
        rows.Where(r => r.Status >= 2).Should().OnlyContain(r => r.SecretBytes == 0, "history rows keep no secret");

        if (deleter == Deleter.ResetMfa)
        {
            // A reset that read the credential before the switch committed is refused as a concurrency conflict and changes nothing;
            // one that read it afterwards removes everything. Either way the outcome is serial.
            if (deleting.Succeeded)
            {
                credentialRows.Should().Be(0);
                rows.Should().BeEmpty();
                codes.Should().BeNull();
            }
            else
            {
                deleting.Error.Should().NotBeNull();
                (await world.ActiveSecretHashAsync()).Should().Be(MfaCandidateWorld.HashOf(protectedB), "the switch's secret is active");
                (await world.ActiveCredentialIdAsync()).Should().Be(newCredentialId);
                codes.Should().Be(switchCodes, "the refused reset changed no code");
            }

            return;
        }

        deleting.Succeeded.Should().BeTrue($"the deleter completes after the switch ({deleting})");
        (await world.ActiveSecretHashAsync()).Should().Be(MfaCandidateWorld.HashOf(protectedB), "the switch's secret is active");
        (await world.ActiveCredentialIdAsync()).Should().Be(newCredentialId).And.NotBe(before.Credential);
        rows.Should().BeEmpty("the deleter ran second and removed every candidate");
        switch (deleter)
        {
            case Deleter.RegenerateRecoveryCodes:
                codes.Should().NotBeNull();
                codes!.Split(',').Should().NotIntersectWith(request.RecoveryCodes.Select(c => c.Id));
                codes.Should().NotBe(codesBefore);
                break;
            case Deleter.DeleteAllRecoveryCodes:
                codes.Should().BeNull();
                break;
            case Deleter.DiscardCandidates:
                codes.Should().Be(switchCodes, "a discard leaves the switch's codes");
                break;
        }
    }

    // ---- Arrangement and observation ----

    private async Task<MfaCandidateWorld> WorldAsync(bool rcsi)
    {
        var world = await MfaCandidateWorld.CreateAsync(_catalogs, rcsi);
        Record(await ServerAsync(world.Scratch, rcsi));
        return world;
    }

    private static RoleAuthorityWorld.PauseAt AfterStageHistoryPurge() =>
        new(c => c.CommandText.Contains("DELETE", StringComparison.Ordinal)
                 && c.CommandText.Contains("[UserTotpReplacementCandidates]", StringComparison.Ordinal)
                 && c.CommandText.Contains("[Status] >= 2", StringComparison.Ordinal), after: true);

    private static async Task<(bool Ok, string Detail)> RunDeleterAsync(Deleter deleter, AppertureDbContext db, UserId user)
    {
        switch (deleter)
        {
            case Deleter.RegenerateRecoveryCodes:
                var regenerated = await Service(db).RegenerateRecoveryCodesAsync(user, AuditActor.EmergencyCli);
                return (true, $"regenerated {regenerated.Count} codes");
            case Deleter.ResetMfa:
                await Service(db).ResetAsync(user, AuditActor.EmergencyCli);
                return (true, "reset");
            case Deleter.DeleteAllRecoveryCodes:
                await OwnTransactionAsync(db, async () => await new SqlNativeMfaRecoveryCodeRepository(db).DeleteAllAsync(user));
                return (true, "deleted codes and candidates");
            case Deleter.DiscardCandidates:
                var discarded = 0;
                await OwnTransactionAsync(db, async () => { discarded = await new SqlNativeMfaReplacementCandidateRepository(db).DiscardAsync(user); });
                return (true, $"discarded {discarded}");
            default:
                throw new ArgumentOutOfRangeException(nameof(deleter), deleter, null);
        }
    }

    private static NativeMfaService Service(AppertureDbContext db) =>
        new(
            new SqlNativeTotpCredentialRepository(db),
            new SqlNativeMfaRecoveryCodeRepository(db),
            new LocalPasswordHasher(),
            new ManagementHost.IntegrationTestSecretProtector(),
            TimeProvider.System,
            NullLogger<NativeMfaService>.Instance,
            AuditRecorderTestSupport.NewUnit(db, null));

    private sealed record CandidateObservation(string Id, int Status, long SecretBytes, string SecretHash)
    {
        public override string ToString() => $"{Id}:status={Status}:secretBytes={SecretBytes}:hash={SecretHash[..Math.Min(12, SecretHash.Length)]}";
    }

    /// <summary>Two candidate ids whose clustered-key order is fixed by a prefix and whose uniqueness comes from a tag.</summary>
    private static (string Low, string High) Keys()
    {
        var tag = Guid.NewGuid().ToString("N")[..24];
        return ("00000000" + tag, "ffffffff" + tag);
    }

    private static async Task SeedCandidateAsync(
        AuditSqlScratch scratch, string candidateId, UserId user, int status, int version, byte[] secret, string stagedAgainst, long? matchedStep)
    {
        await using var connection = new SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO [UserTotpReplacementCandidates]
                ([CandidateId], [UserId], [ProtectedSecret], [ProtectorKeyId], [StagedAgainstCredentialId], [Status], [Version],
                 [CreatedUtc], [ExpiresAtUtc], [VerifiedUtc], [MatchedStep])
            VALUES (@id, @user, @secret, @key, @against, @status, @version,
                    SYSUTCDATETIME(), DATEADD(MINUTE, 10, SYSUTCDATETIME()), CASE WHEN @status >= 1 THEN SYSUTCDATETIME() END, @step);
            """;
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 64) { Value = candidateId });
        command.Parameters.Add(new SqlParameter("@user", SqlDbType.NVarChar, 64) { Value = user.Value });
        command.Parameters.Add(new SqlParameter("@secret", SqlDbType.VarBinary, -1) { Value = secret });
        command.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 128) { Value = NativeMfaService.TotpProtectorKeyId });
        command.Parameters.Add(new SqlParameter("@against", SqlDbType.NVarChar, 64) { Value = stagedAgainst });
        command.Parameters.Add(new SqlParameter("@status", SqlDbType.Int) { Value = status });
        command.Parameters.Add(new SqlParameter("@version", SqlDbType.Int) { Value = version });
        command.Parameters.Add(new SqlParameter("@step", SqlDbType.BigInt) { Value = (object?)matchedStep ?? DBNull.Value });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<CandidateObservation>> CandidatesAsync(AuditSqlScratch scratch, UserId user)
    {
        var rows = await scratch.RowsAsync($"""
            SELECT CandidateId, Status, DATALENGTH(ProtectedSecret) AS SecretBytes,
                   CONVERT(nvarchar(64), HASHBYTES('SHA2_256', ProtectedSecret), 2) AS SecretHash
              FROM UserTotpReplacementCandidates WHERE UserId = N'{user.Value}' ORDER BY CandidateId;
            """);
        return rows.Select(r => new CandidateObservation(
            (string)r["CandidateId"]!,
            Convert.ToInt32(r["Status"]),
            Convert.ToInt64(r["SecretBytes"] ?? 0L),
            (string?)r["SecretHash"] ?? "")).ToList();
    }

    private static Task<int> CredentialCountAsync(AuditSqlScratch scratch, UserId user) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{user.Value}';");

    private void Record(string line) => _output.WriteLine("LOCKORDER " + line);

    private void RecordRun(Interleaving run)
    {
        Record($"  parked: {run.Parked}");
        Record($"  other:  {run.Other}");
        Record($"  other when the parked side was released: {run.OtherWhenReleased}");
        Record($"  requests while parked: {run.RequestsWhileParked}");
    }
}
