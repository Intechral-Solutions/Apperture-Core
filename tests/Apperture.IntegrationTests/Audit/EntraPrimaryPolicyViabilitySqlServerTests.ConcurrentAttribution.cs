using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP4A review, L-1: what the policy says about a refusal that the user did not cause. The Entra-primary policy compares the
// state it read under the gate before its write with the state it reads after. A writer that is exempt from the gate (the
// consumption of the last Recovery Access recovery code at its sign-in, ruling C-14) or not yet under it can change that state in
// between, so a user who was never an Entra-independent administrator can be refused. The credential then stays enabled as for any
// refusal, but the last-administrator attempt row would not be true of that user, so none is written and the caller warns instead.
// Every case parks the policy between its two reads (before the write of the credential, holding the operation lock and the gate)
// and makes the other change then. The recovery code is consumed by the real service, in its own audited unit, to show that the
// exempt path is not held up by the gate.
public sealed partial class EntraPrimaryPolicyViabilitySqlServerTests
{
    private const string RecoveryCode = "ABCD-EFGH-JKLM";

    private static PauseAt BetweenTheReads() => PauseAt.BeforeStatement("UPDATE [LocalCredentials]");

    /// <summary>Replaces the placeholder hash of the healthy Recovery Access account's code with the hash of a code that can be presented.</summary>
    private static async Task GiveTheRecoveryAccountAUsableCodeAsync(RoleAuthorityWorld world, string recoveryUserId)
    {
        var (hash, salt) = new LocalPasswordHasher().HashPassword(MfaRecoveryCodes.Normalize(RecoveryCode));
        await world.Scratch.ExecuteAsync(
            $"UPDATE UserMfaRecoveryCodes SET CodeHash = N'{hash}', Salt = N'{salt}' WHERE UserId = N'{recoveryUserId}' AND ConsumedAtUtc IS NULL;");
    }

    /// <summary>A legitimate Recovery Access sign-in presenting its last unused code (ruling C-14): its own unit, no gate.</summary>
    private static async Task<bool> ConsumeTheRecoveryCodeAsync(AuditSqlScratch scratch, string recoveryUserId)
    {
        await using var db = scratch.NewDb();
        var mfa = new NativeMfaService(
            new SqlNativeTotpCredentialRepository(db),
            new SqlNativeMfaRecoveryCodeRepository(db),
            new LocalPasswordHasher(),
            new NoSecretProtector(),
            TimeProvider.System,
            NullLogger<NativeMfaService>.Instance,
            NewUnit(db, null));
        return await mfa.ConsumeRecoveryCodeAsync(new UserId(recoveryUserId), RecoveryCode);
    }

    private sealed class NoSecretProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>Parks the application between its two reads, runs <paramref name="between"/> while it holds the gate, then lets it finish.</summary>
    private static async Task<T> BetweenTheReadsAsync<T>(AuditSqlScratch scratch, Func<AppertureDbContext, Task<T>> apply, Func<Task> between)
    {
        var pause = BetweenTheReads();
        var application = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            return await apply(db);
        });
        await pause.Reached.WaitAsync(Patience);
        (await EntraRegistrationViabilityWp3SqlServerTests.ProbeGateAsync(scratch)).Should().Be(-1, "the application holds the administrator-set gate while it is parked");

        await between().WaitAsync(Patience);

        pause.Release();
        return await application.WaitAsync(Patience);
    }

    // ------------------------------------------------------------------------------------------------ the required race

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task L1_the_last_recovery_code_consumed_between_the_reads_defers_a_user_who_is_not_an_administrator_without_the_last_administrator_row(bool rcsi)
    {
        // Recovery Access is healthy, nobody is an ordinary administrator, and the target holds no role, so the target contributes to
        // no viability set in either view.
        var world = await FreshAsync(rcsi);
        var recovery = await RecoveryAccountAsync(world, healthy: true);
        await GiveTheRecoveryAccountAUsableCodeAsync(world, recovery);
        var target = await OrdinaryUserAsync(world, "target");
        var hash = await HashOfAsync(world.Scratch, target);
        var before = await ViabilityAsync(world.Scratch);
        before.RecoveryHealthy.Should().BeTrue();
        before.Now.EntraIndependentAdministrators.Should().BeEmpty();
        before.Now.ViableOrdinaryAdministrators.Should().BeEmpty();

        var consumed = false;
        var outcome = await BetweenTheReadsAsync(
            world.Scratch,
            db => PolicyOver(world.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(target)).AsTask(),
            async () => consumed = await ConsumeTheRecoveryCodeAsync(world.Scratch, recovery));

        consumed.Should().BeTrue("the exempt path was not held up by the gate, and the code was the real one");
        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange);
        (await ViabilityAsync(world.Scratch)).RecoveryHealthy.Should().BeFalse("the consumption did happen between the reads");
        (await world.CredentialDisabledAsync(target)).Should().BeFalse("the disable was rolled back with the unit");
        (await HashOfAsync(world.Scratch, target)).Should().Be(hash);
        (await SuccessRowsAsync(world, target)).Should().Be(0);
        (await DeferralRowsAsync(world, target)).Should().Be(0, "the last-administrator row is not true of a user who is not an administrator");
        (await world.AuditRowsAsync(AuditActionNames.RecoveryCodeConsumed, recovery)).Should().Be(1, "the exempt change is recorded as its own event");

        // The policy runs at every Entra sign-in: the next one decides on the state as it is then, and the user has nothing to lose.
        (await ApplyAsync(world.Scratch, target)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        (await world.CredentialDisabledAsync(target)).Should().BeTrue();
        (await SuccessRowsAsync(world, target)).Should().Be(1);
        (await DeferralRowsAsync(world, target)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task L1_the_sign_in_succeeds_and_the_caller_warns_once_when_the_policy_is_deferred_by_a_concurrent_change(bool rcsi)
    {
        // The caller is the proof of a pending Entra link at sign-in: it resolves the session whatever the policy reports, and says
        // what it was told once.
        var world = await FreshAsync(rcsi);
        var recovery = await RecoveryAccountAsync(world, healthy: true);
        await GiveTheRecoveryAccountAUsableCodeAsync(world, recovery);
        var target = await world.NewUserAsync("signin");
        await world.AddCredentialAsync(target);
        var objectId = Guid.NewGuid().ToString("D");
        await world.Scratch.ExecuteAsync($"""
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt, LastAuthenticatedAt)
            VALUES (N'P{target}', N'{target}', 0, N'{objectId}', N'{Tenant}', SYSDATETIMEOFFSET(), NULL);
            """);
        var log = new CapturingLogger<SqlPrincipalToUserResolver>();
        var actor = new HumanActor(new ActorId(target), "Sign In", IdentitySource.Entra, tenantId: Tenant, entraObjectId: objectId);

        var resolved = await BetweenTheReadsAsync(
            world.Scratch,
            db => new SqlPrincipalToUserResolver(
                    new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
                    PolicyOver(world.Scratch, db),
                    log,
                    NewUnit(db, null))
                .ResolveUserIdAsync(actor).AsTask(),
            async () => (await ConsumeTheRecoveryCodeAsync(world.Scratch, recovery)).Should().BeTrue());

        resolved.Should().Be(new UserId(target), "a deferral never fails the sign-in");
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM IdentityLinks WHERE UserId = N'{target}' AND Source = 0 AND LastAuthenticatedAt IS NOT NULL;"))
            .Should().Be(1, "the link was proven before the policy ran");
        (await world.CredentialDisabledAsync(target)).Should().BeFalse();
        (await SuccessRowsAsync(world, target)).Should().Be(0);
        (await DeferralRowsAsync(world, target)).Should().Be(0);
        var warning = log.Entries.Should().ContainSingle(e => e.Level >= LogLevel.Warning).Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain(target).And.Contain("administrator access changed while it was being applied");
        warning.Message.Should().NotContainEquivalentOf("last administrator").And.NotContainEquivalentOf("last Entra-independent");
    }

    // ------------------------------------------------------------------------------------- the same change, a genuine deferral

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task L1_a_user_who_is_the_last_entra_independent_administrator_keeps_the_attempt_row_when_the_last_recovery_code_is_consumed_too(bool rcsi)
    {
        // Healthy Recovery Access satisfied I-FALLBACK, and the target was also the one ordinary administrator who signs in without
        // Microsoft. Disabling their credential would have been allowed on the posture alone; with the code gone it is the last
        // such administrator, and the established row is true of them.
        var world = await FreshAsync(rcsi);
        var recovery = await RecoveryAccountAsync(world, healthy: true);
        await GiveTheRecoveryAccountAUsableCodeAsync(world, recovery);
        var target = await BothWaysAdminAsync(world, "sole");
        (await ViabilityAsync(world.Scratch)).Now.EntraIndependentAdministrators.Should().BeEquivalentTo([target]);

        var outcome = await BetweenTheReadsAsync(
            world.Scratch,
            db => PolicyOver(world.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(target)).AsTask(),
            async () => (await ConsumeTheRecoveryCodeAsync(world.Scratch, recovery)).Should().BeTrue());

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        (await world.CredentialDisabledAsync(target)).Should().BeFalse();
        (await SuccessRowsAsync(world, target)).Should().Be(0);
        (await DeferralRowsAsync(world, target)).Should().Be(1, "the target is the last Entra-independent administrator in the state read under the gate");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task L1_a_user_who_is_the_last_viable_administrator_keeps_the_attempt_row_and_its_outcome_when_another_change_happens_too(bool rcsi)
    {
        // The target's own Entra link is pending, so its Local credential is the only way in (I-VIABLE), whatever Recovery Access says.
        var world = await FreshAsync(rcsi);
        var recovery = await RecoveryAccountAsync(world, healthy: true);
        await GiveTheRecoveryAccountAUsableCodeAsync(world, recovery);
        var target = await BothWaysAdminAsync(world, "pending", proven: false);

        var outcome = await BetweenTheReadsAsync(
            world.Scratch,
            db => PolicyOver(world.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(target)).AsTask(),
            async () => (await ConsumeTheRecoveryCodeAsync(world.Scratch, recovery)).Should().BeTrue());

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator);
        (await world.CredentialDisabledAsync(target)).Should().BeFalse();
        (await DeferralRowsAsync(world, target)).Should().Be(1);
        (await SuccessRowsAsync(world, target)).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------- the viable-administrator branch

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task L1_the_removal_of_the_only_viable_administrator_between_the_reads_is_not_attributed_to_a_user_who_is_not_an_administrator(bool rcsi)
    {
        // A writer that is not yet under the gate (WP4B) takes the only administrator who can sign in away while the policy is parked.
        var world = await FreshAsync(rcsi);
        var administrator = await LocalOnlyAdminAsync(world, "only");
        var target = await OrdinaryUserAsync(world, "target");

        var outcome = await BetweenTheReadsAsync(
            world.Scratch,
            db => PolicyOver(world.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(target)).AsTask(),
            () => world.Scratch.ExecuteAsync($"DELETE FROM UserRoles WHERE UserId = N'{administrator}' AND RoleId = N'{Admin}';"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange);
        (await world.CredentialDisabledAsync(target)).Should().BeFalse();
        (await SuccessRowsAsync(world, target)).Should().Be(0);
        (await DeferralRowsAsync(world, target)).Should().Be(0);
    }
}
