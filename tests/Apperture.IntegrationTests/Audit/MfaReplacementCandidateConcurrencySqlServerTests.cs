using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// Concurrent staging, verification and switching of an authenticator replacement, against real SQL Server on both isolation
/// modes. Every case parks one operation at a statement while it holds what it has taken, lets another operation try, and shows what
/// each of them ends with. A "blocked" assertion is only that the second operation had not finished while the first held its lock;
/// the end state is what is asserted for correctness. These cases run on the production retrying contexts, so a deadlock retried
/// away would not fail them; the one case that claims "without deadlock" counts the retried deadlock victims and asserts there were
/// none, and the lock order against every candidate deleter is proved on contexts without retry in
/// <see cref="MfaReplacementCandidateLockOrderSqlServerTests"/>. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class MfaReplacementCandidateConcurrencySqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BlockedFor = TimeSpan.FromMilliseconds(1500);

    private readonly AuditIsolationCatalogs _catalogs;

    public MfaReplacementCandidateConcurrencySqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private Task<MfaCandidateWorld> WorldAsync(bool rcsi) => MfaCandidateWorld.CreateAsync(_catalogs, rcsi);

    /// <summary>The first locking read of the user's credential row: the first thing every candidate operation does, taken inside its transaction.</summary>
    private static PauseAt AfterTheCredentialLock() =>
        new(c => c.CommandText.Contains("[UserTotpCredentials] WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal), after: true);

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

    private static bool ChainContains<T>(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is T)
                return true;
        }

        return false;
    }

    /// <summary>True when the operation has not finished after a short wait: it is waiting for a lock the parked operation holds.</summary>
    private static async Task<bool> StillWaitingAsync(Task operation)
    {
        var first = await Task.WhenAny(operation, Task.Delay(BlockedFor));
        return first != operation;
    }

    private static Task<NativeMfaCandidateStageResult> StageParkedAsync(MfaCandidateWorld world, PauseAt? pause, byte[] secret) =>
        Task.Run(async () =>
        {
            await using var db = pause is null ? world.Scratch.NewDb() : world.Scratch.NewDb(pause);
            return await new SqlNativeMfaReplacementCandidateRepository(db).StageAsync(world.UserId, secret, NativeMfaService.TotpProtectorKeyId, TimeSpan.FromMinutes(10));
        });

    private static Task<NativeMfaCandidateVerifyResult> VerifyParkedAsync(MfaCandidateWorld world, PauseAt? pause, string id, int version, long step) =>
        Task.Run(async () =>
        {
            await using var db = pause is null ? world.Scratch.NewDb() : world.Scratch.NewDb(pause);
            return await new SqlNativeMfaReplacementCandidateRepository(db).MarkVerifiedAsync(world.UserId, id, version, step);
        });

    private static Task<NativeMfaSwitchResult> SwitchParkedAsync(MfaCandidateWorld world, PauseAt? pause, NativeMfaSwitchRequest request) =>
        Task.Run(async () =>
        {
            await using var db = pause is null ? world.Scratch.NewDb() : world.Scratch.NewDb(pause);
            return await new SqlNativeMfaReplacementCandidateRepository(db).SwitchAsync(request);
        });

    // ---- Staging against staging ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Two_stagings_for_one_user_run_one_after_the_other_and_leave_exactly_one_live_candidate(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var pause = AfterTheCredentialLock();
        var first = StageParkedAsync(world, pause, (await world.NewSecretAsync()).Protected);
        await ReachedAsync(pause, first);

        var second = StageParkedAsync(world, null, (await world.NewSecretAsync()).Protected);
        (await StillWaitingAsync(second)).Should().BeTrue("the second staging waits for the user's credential row the first one holds");
        pause.Release();

        var firstResult = await first.WaitAsync(Patience);
        var secondResult = await second.WaitAsync(Patience);

        firstResult.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        secondResult.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        (await world.LiveCandidateCountAsync()).Should().Be(1, "the filtered unique index was never needed to stop a second live candidate");
        (await world.GetAsync(firstResult.Candidate!.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Superseded, "the later staging superseded the earlier one");
        (await world.GetAsync(secondResult.Candidate!.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Staged);
    }

    // ---- Verification against staging ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_staging_waits_for_a_verification_in_progress_and_then_supersedes_the_candidate_just_verified(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var candidate = await world.StageAsync();
        var active = await world.ActiveAsync();
        var pause = AfterTheCredentialLock();
        var verify = VerifyParkedAsync(world, pause, candidate.CandidateId, candidate.Version, MfaCandidateWorld.CurrentStep());
        await ReachedAsync(pause, verify);

        var stage = StageParkedAsync(world, null, (await world.NewSecretAsync()).Protected);
        (await StillWaitingAsync(stage)).Should().BeTrue();
        pause.Release();

        var verified = await verify.WaitAsync(Patience);
        var staged = await stage.WaitAsync(Patience);

        verified.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        staged.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        (await world.GetAsync(candidate.CandidateId))!.Status.Should().Be(NativeMfaCandidateStatus.Superseded, "the verified candidate was superseded by the newer staging");
        (await world.SwitchAsync(world.Request(candidate.CandidateId, verified.Version!.Value))).Outcome.Should().Be(NativeMfaCandidateOutcome.Superseded);
        (await world.ActiveAsync()).Should().Be(active);
        (await world.LiveCandidateCountAsync()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Of_two_verifications_holding_the_same_version_exactly_one_wins(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var candidate = await world.StageAsync();
        var pause = AfterTheCredentialLock();
        var first = VerifyParkedAsync(world, pause, candidate.CandidateId, candidate.Version, 1001);
        await ReachedAsync(pause, first);

        var second = VerifyParkedAsync(world, null, candidate.CandidateId, candidate.Version, 2002);
        (await StillWaitingAsync(second)).Should().BeTrue();
        pause.Release();

        var firstResult = await first.WaitAsync(Patience);
        var secondResult = await second.WaitAsync(Patience);

        firstResult.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        secondResult.Outcome.Should().Be(NativeMfaCandidateOutcome.VersionMismatch, "the second holder presented a version that no longer exists");
        (await world.GetAsync(candidate.CandidateId))!.MatchedStep.Should().Be(1001, "the winner's matched step is the one recorded");
        (await world.GetAsync(candidate.CandidateId))!.Version.Should().Be(candidate.Version + 1);
    }

    // ---- Switching ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_staging_waits_for_a_switch_in_progress_and_binds_its_candidate_to_the_new_credential(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var (candidate, version) = await world.StageAndVerifyAsync();
        var request = world.Request(candidate.CandidateId, version);
        var pause = AfterTheCredentialLock();
        var switching = SwitchParkedAsync(world, pause, request);
        await ReachedAsync(pause, switching);

        var stage = StageParkedAsync(world, null, (await world.NewSecretAsync()).Protected);
        (await StillWaitingAsync(stage)).Should().BeTrue("the staging waits for the switch, so it cannot bind to the credential being replaced");
        pause.Release();

        var switched = await switching.WaitAsync(Patience);
        var staged = await stage.WaitAsync(Patience);

        switched.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        staged.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        staged.Candidate!.StagedAgainstCredentialId.Should().Be(switched.NewCredentialId, "the new candidate replaces the new authenticator, not the one just replaced");
        (await world.ActiveCredentialIdAsync()).Should().Be(switched.NewCredentialId);
        (await world.GetAsync(candidate.CandidateId)).Should().BeNull("the consumed candidate was history, purged by the staging that followed the switch");
        (await world.LiveCandidateCountAsync()).Should().Be(1);
        (await world.CandidateRowCountAsync()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Two_switches_of_one_candidate_apply_once_and_the_other_finds_it_consumed(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var (candidate, version) = await world.StageAndVerifyAsync();
        var firstRequest = world.Request(candidate.CandidateId, version, "first");
        var secondRequest = world.Request(candidate.CandidateId, version, "second");
        var pause = AfterTheCredentialLock();
        var first = SwitchParkedAsync(world, pause, firstRequest);
        await ReachedAsync(pause, first);

        var second = SwitchParkedAsync(world, null, secondRequest);
        (await StillWaitingAsync(second)).Should().BeTrue();
        pause.Release();

        var firstResult = await first.WaitAsync(Patience);
        var secondResult = await second.WaitAsync(Patience);

        firstResult.Outcome.Should().Be(NativeMfaCandidateOutcome.Applied);
        secondResult.Outcome.Should().Be(NativeMfaCandidateOutcome.Consumed);
        (await world.ActiveCredentialIdAsync()).Should().Be(firstResult.NewCredentialId);
        (await world.CodeIdsAsync()).Should().Be(string.Join(",", firstRequest.RecoveryCodes.Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal)), "only the winner's codes exist");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reset_that_overlaps_a_switch_fails_as_a_concurrency_conflict_changing_nothing_and_a_retry_removes_everything(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var (candidate, version) = await world.StageAndVerifyAsync();
        var request = world.Request(candidate.CandidateId, version);
        var pause = AfterTheCredentialLock();
        var switching = SwitchParkedAsync(world, pause, request);
        await ReachedAsync(pause, switching);

        // The reset read the credential row before the switch rewrote it; its tracked delete is conditional on the row it read.
        var reset = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb();
            await world.Mfa(db).ResetAsync(world.UserId, AuditActor.EmergencyCli);
        });
        (await StillWaitingAsync(reset)).Should().BeTrue("the reset waits for the switch to finish with the credential row");
        pause.Release();

        (await switching.WaitAsync(Patience)).IsApplied.Should().BeTrue();
        var overlapped = async () => await reset.WaitAsync(Patience);
        var failure = (await overlapped.Should().ThrowAsync<Exception>()).Which;
        ChainContains<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>(failure)
            .Should().BeTrue($"the reset failed as a concurrency conflict, not otherwise: {failure.GetType().Name}: {failure.Message}");

        (await world.AuditRowsAsync(AuditActionNames.MfaReset)).Should().Be(0, "the failed reset recorded nothing");
        (await world.ActiveSecretHashAsync()).Should().Be(MfaCandidateWorld.HashOf(candidate.ProtectedSecret), "the switch stands");
        (await world.CodeIdsAsync()).Should().Be(string.Join(",", request.RecoveryCodes.Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal)));

        // Retried against the committed state, the reset removes the authenticator, its codes and the consumed candidate.
        await using (var db = world.Scratch.NewDb())
            await world.Mfa(db).ResetAsync(world.UserId, AuditActor.EmergencyCli);
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{world.UserId.Value}';")).Should().Be(0);
        (await world.CandidateRowCountAsync()).Should().Be(0);
        (await world.CodeIdsAsync()).Should().BeNull();
        (await world.AuditRowsAsync(AuditActionNames.MfaReset)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_regeneration_of_the_recovery_codes_waits_for_a_switch_in_progress_without_deadlock_and_its_codes_win(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var (candidate, version) = await world.StageAndVerifyAsync();
        var request = world.Request(candidate.CandidateId, version);
        // Parked after the candidate was consumed and before the old codes are deleted: the switch holds the credential and candidate rows.
        var pause = PauseAt.BeforeStatement("DELETE FROM [UserMfaRecoveryCodes]");
        // Both sides retry as production does, and count every deadlock victim they retry: "without deadlock" is asserted, not assumed.
        var deadlocks = new RoleAuthorityWorld.DeadlockLog();
        var switching = Task.Run(async () =>
        {
            await using var db = world.World.NewDeadlockAwareDb(deadlocks, pause);
            return await new SqlNativeMfaReplacementCandidateRepository(db).SwitchAsync(request);
        });
        await ReachedAsync(pause, switching);

        IReadOnlyList<string>? regenerated = null;
        var regeneration = Task.Run(async () =>
        {
            await using var db = world.World.NewDeadlockAwareDb(deadlocks);
            regenerated = await world.Mfa(db).RegenerateRecoveryCodesAsync(world.UserId, AuditActor.EmergencyCli);
        });
        (await StillWaitingAsync(regeneration)).Should().BeTrue("the regeneration takes the user's credential row before the candidates, and the switch holds it");
        pause.Release();

        (await switching.WaitAsync(Patience)).IsApplied.Should().BeTrue();
        await regeneration.WaitAsync(Patience);

        deadlocks.Victims.Should().Be(0, "neither side was chosen as a deadlock victim and retried");
        regenerated.Should().HaveCount(10);
        (await world.ActiveSecretHashAsync()).Should().Be(MfaCandidateWorld.HashOf(candidate.ProtectedSecret), "the switch's secret is the active one");
        (await world.CodeIdsAsync())!.Split(',').Should().NotIntersectWith(request.RecoveryCodes.Select(c => c.Id), "the later regeneration's codes replaced the switch's");
        (await world.CandidateRowCountAsync()).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_switch_waits_for_a_regeneration_in_progress_and_finds_its_candidate_gone_changing_nothing(bool rcsi)
    {
        var world = await WorldAsync(rcsi);
        var (candidate, version) = await world.StageAndVerifyAsync();
        var activeSecret = await world.ActiveSecretHashAsync();
        var credential = await world.ActiveCredentialIdAsync();
        // The regeneration has removed the candidate (uncommitted) and is about to replace the codes.
        var pause = PauseAt.BeforeStatement("DELETE FROM [UserMfaRecoveryCodes]");
        var regeneration = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            return await world.Mfa(db).RegenerateRecoveryCodesAsync(world.UserId, AuditActor.EmergencyCli);
        });
        await ReachedAsync(pause, regeneration);

        var switching = SwitchParkedAsync(world, null, world.Request(candidate.CandidateId, version));
        (await StillWaitingAsync(switching)).Should().BeTrue("the switch waits for the row the regeneration removed");
        pause.Release();

        await regeneration.WaitAsync(Patience);
        var result = await switching.WaitAsync(Patience);

        result.Outcome.Should().Be(NativeMfaCandidateOutcome.NotFound);
        (await world.ActiveSecretHashAsync()).Should().Be(activeSecret, "the switch did not replace the secret");
        (await world.ActiveCredentialIdAsync()).Should().Be(credential);
    }
}
