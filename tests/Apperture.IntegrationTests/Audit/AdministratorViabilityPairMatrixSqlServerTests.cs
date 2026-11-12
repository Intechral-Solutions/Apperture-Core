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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP5: the pairs of viability-affecting writers the earlier packages did not pair, against real SQL Server on a locking READ
/// COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog (design gate §7, A10.2b.1, A10.2b.9, A10.2b.11, A10.2b.13). Each writer runs
/// in the composition it runs in for real: the administrator writers and the Entra-primary policy as the Management Host composes them (the
/// host's running options), the Recovery Access and authenticator writers as the emergency command line composes them (the attestation the
/// host wrote). Both compositions take the one administrator-set gate in the same database.
/// <para>
/// Every concurrent case forces the overlap: the first writer is parked inside its unit with everything it holds (the operation lock, the
/// gate, its row locks), after its decision and before its commit, until the second is shown waiting on the application lock and on no row.
/// The second is then decided on what the first committed. Every case has a twin that runs the same two one after the other and must give
/// the same result. What a case concludes is read back with SQL of its own. Fails, never skips, without SQL Server.
/// </para>
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed partial class AdministratorViabilityPairMatrixSqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private const string Admin = ServiceViabilityWorld.Admin;
    private const string Tenant = ServiceViabilityWorld.Tenant;
    private const string Applied = "applied";
    private const string FallbackRequired = "refused:" + GovernanceConflictCodes.EntraIndependentFallbackRequired;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly ActorId Cli = new(EmergencyAccessService.CliActorId);

    private readonly AuditIsolationCatalogs _catalogs;

    public AdministratorViabilityPairMatrixSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    /// <summary>A failing case must not leave an operation parked with the gate.</summary>
    public void Dispose() => PauseAt.ReleaseAll();

    // ================================================================================================ the writers

    /// <summary>The viability-affecting writers this suite pairs.</summary>
    public enum Writer
    {
        /// <summary>The Management Host: an administrator removes the Platform Administrator role.</summary>
        RemoveRole,

        /// <summary>The Management Host: an administrator disables a Local credential.</summary>
        DisableCredential,

        /// <summary>The Management Host: an administrator removes a Local identity link.</summary>
        RemoveLocalLink,

        /// <summary>The Management Host, system-driven: the Entra-primary policy disables a Local credential after an Entra proof.</summary>
        EntraPrimaryPolicy,

        /// <summary>The emergency command line: Recovery Access designation.</summary>
        Designate,

        /// <summary>The emergency command line: strict Recovery Access revocation.</summary>
        Revoke,

        /// <summary>The emergency command line: the bare reset of the authenticator.</summary>
        BareReset,

        /// <summary>The emergency command line: the switch to a verified replacement authenticator.</summary>
        Switch,
    }

    public sealed record Op(Writer Kind, string Target);

    /// <summary>The service's clock, moved forward between steps so each authenticator code falls in a step of its own.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public long Unix => _now.ToUnixTimeSeconds();

        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    /// <summary>One case's catalog and people.</summary>
    private sealed class World
    {
        public required ServiceViabilityWorld W { get; init; }

        public RoleAuthorityWorld R => W.World;

        public AuditSqlScratch Scratch => W.Scratch;

        /// <summary>An administrator who holds the role (so may act) but cannot sign in (so counts for nothing).</summary>
        public required string Actor { get; init; }

        public Clock Clock { get; } = new();

        /// <summary>The recovery codes the Recovery Access account's enrolment showed, when it was enrolled through the service.</summary>
        public IReadOnlyList<string> RecoveryCodes { get; set; } = [];

        /// <summary>A replacement authenticator staged and verified for the Recovery Access account before the race.</summary>
        public (string CandidateId, int Version)? Candidate { get; set; }
    }

    /// <summary>
    /// A catalog cleared of administrator state, with the active registration for <see cref="Tenant"/> and the attestation of a host whose Local
    /// and Microsoft sign-in are both established for it, so both compositions read the same configuration, and the writers' actor.
    /// </summary>
    private async Task<World> WorldAsync(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        await w.AttestAsync(ServiceViabilityWorld.Established(), registration: true);
        var actor = await w.World.NewUserAsync("actor", Admin);
        await w.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{actor}';");
        return new World { W = w, Actor = actor };
    }

    /// <summary>An ordinary administrator who signs in both with a Local credential and through a proven Microsoft link for <see cref="Tenant"/>.</summary>
    private static async Task<string> BothWaysAsync(World w, string stem)
    {
        var id = await w.W.EntraIndependentAdminAsync(stem);
        await w.R.AddEntraLinkAsync(id, Tenant, proven: true);
        return id;
    }

    /// <summary>An ordinary administrator who signs in only through Microsoft: viable, never Entra-independent, so I-VIABLE never decides.</summary>
    private static Task<string> EntraOnlyAsync(World w, string stem) => w.W.EntraOnlyAdminAsync(stem);

    /// <summary>The designated Recovery Access account with the role and a Local credential but no authenticator: not healthy.</summary>
    private static async Task<string> UnhealthyRecoveryAsync(World w)
    {
        var id = await w.W.EntraIndependentAdminAsync("recovery");
        await w.R.DesignateRecoveryAccessAsync(id);
        return id;
    }

    /// <summary>The designated Recovery Access account, its authenticator enrolled through the service (ten unused codes): healthy.</summary>
    private static async Task<string> HealthyRecoveryAsync(World w)
    {
        var id = await w.W.EntraIndependentAdminAsync("recovery");
        await w.R.DesignateRecoveryAccessAsync(id);
        await using var db = w.Scratch.NewDb();
        var mfa = w.W.Mfa(db, time: w.Clock);
        var challenge = await mfa.BeginEnrollmentAsync(new UserId(id), "account");
        var secret = Base32.Decode(challenge.ManualEntryKey);
        w.RecoveryCodes = await mfa.CompleteEnrollmentAsync(new UserId(id), Rfc6238Totp.Compute(secret, w.Clock.Unix), AuditActor.EmergencyCli);
        return id;
    }

    /// <summary>Stages a replacement authenticator for <paramref name="userId"/> and proves possession of it, a step later than anything it used.</summary>
    private static async Task StageVerifiedReplacementAsync(World w, string userId)
    {
        w.Clock.Advance(60);
        await using var db = w.Scratch.NewDb();
        var mfa = w.W.Mfa(db, time: w.Clock);
        var challenge = await mfa.BeginReplacementAsync(new UserId(userId), "account");
        var secret = Base32.Decode(challenge.ManualEntryKey);
        var verified = await mfa.VerifyReplacementAsync(new UserId(userId), challenge.CandidateId, challenge.Version, Rfc6238Totp.Compute(secret, w.Clock.Unix));
        w.Candidate = (challenge.CandidateId, verified.Version);
    }

    // ---- The two compositions over one database ----

    /// <summary>The host's reading: Local sign-in on, the running Microsoft handler bound to <see cref="Tenant"/>.</summary>
    private static DeploymentAuthenticationReading HostReading => AdministratorViabilitySqlServerTests.Reading(Tenant);

    private static UserManagementService HostAdministration(World w, AppertureDbContext db, IPlatformAdministratorSetGate? gate) =>
        w.R.Service(
            db,
            w.Actor,
            advisoryAllowsAll: true,
            gate: gate,
            deployment: HostReading,
            strongAuthentication: new EntraRegistrationViabilityWp3SqlServerTests.AllowingStrongAuthentication());

    private static EntraPrimaryAuthenticationPolicy HostPolicy(World w, AppertureDbContext db, IPlatformAdministratorSetGate? gate)
    {
        var resolver = EntraRegistrationSqlHarness.SecretResolver(db);
        return new EntraPrimaryAuthenticationPolicy(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlLocalCredentialRepository(db),
            NullLogger<EntraPrimaryAuthenticationPolicy>.Instance,
            EntraRegistrationSqlHarness.Viability(db, reading: HostReading, resolver: resolver, gate: gate),
            AdministratorViabilitySqlServerTests.Service(db, HostReading, resolver),
            NewUnit(db, null),
            new SqlAuditEventWriter(w.Scratch.Options(), null, HarnessCatalog, null, null, null));
    }

    /// <summary>The command line's coordinator: the configuration only from the attestation; <paramref name="gate"/> replaces the gate in a control.</summary>
    private static AdministratorViabilityCoordinator CommandLineCoordinator(AppertureDbContext db, IPlatformAdministratorSetGate? gate) =>
        gate is null
            ? ServiceViabilityWorld.Coordinator(db)
            : new AdministratorViabilityCoordinator(
                gate,
                new AdministratorViabilityService(
                    new SqlAdministratorFactsReader(db),
                    new EffectiveSignInConfigurationProvider(
                        new AttestedDeploymentAuthenticationConfiguration(new SqlHostConfigurationAttestationStore(db)),
                        new SqlEntraInstanceRegistrationRepository(db))));

    /// <summary>
    /// Runs one writer on a context of its own (with <paramref name="pause"/> when given) and says what became of it: <c>applied</c>,
    /// <c>refused:&lt;code&gt;</c>, or the policy's outcome as <c>policy:&lt;outcome&gt;</c>. Anything else is a failure of the case.
    /// </summary>
    private static async Task<string> RunAsync(World w, Op op, PauseAt? pause = null, IPlatformAdministratorSetGate? gate = null)
    {
        await using var db = pause is null ? w.Scratch.NewDb() : w.Scratch.NewDb(pause);
        try
        {
            switch (op.Kind)
            {
                case Writer.RemoveRole:
                    await HostAdministration(w, db, gate).RemoveRoleAsync(new UserId(op.Target), new RoleId(Admin));
                    return Applied;
                case Writer.DisableCredential:
                    await HostAdministration(w, db, gate).SetLocalCredentialDisabledAsync(new UserId(op.Target), disabled: true);
                    return Applied;
                case Writer.RemoveLocalLink:
                    var username = await w.Scratch.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{op.Target}';");
                    await HostAdministration(w, db, gate).RemoveIdentityLinkAsync(new UserId(op.Target), IdentitySource.Local, username);
                    return Applied;
                case Writer.EntraPrimaryPolicy:
                    return "policy:" + await HostPolicy(w, db, gate).ApplyAfterEntraProvenAsync(new UserId(op.Target));
                case Writer.Designate:
                    await w.W.Emergency(db, coordinator: CommandLineCoordinator(db, gate)).DesignateAsync(new UserId(op.Target), Cli);
                    return Applied;
                case Writer.Revoke:
                    await w.W.Emergency(db, coordinator: CommandLineCoordinator(db, gate)).RevokeAsync(confirmFallback: true, Cli);
                    return Applied;
                case Writer.BareReset:
                    await w.W.Mfa(db, coordinator: CommandLineCoordinator(db, gate), time: w.Clock).ResetAsync(new UserId(op.Target), AuditActor.EmergencyCli);
                    return Applied;
                case Writer.Switch:
                    var (candidateId, version) = w.Candidate ?? throw new InvalidOperationException("No replacement was staged for the switch.");
                    var result = await w.W.Mfa(db, coordinator: CommandLineCoordinator(db, gate), time: w.Clock)
                        .CompleteReplacementAsync(new UserId(op.Target), candidateId, version, AuditActor.EmergencyCli);
                    result.AlreadyApplied.Should().BeFalse("this is the first switch of the candidate");
                    return Applied;
                default:
                    throw new ArgumentOutOfRangeException(nameof(op));
            }
        }
        catch (GovernanceConflictException refusal)
        {
            return "refused:" + refusal.Code;
        }
    }

    /// <summary>Whether the effect of <paramref name="op"/> is in the catalog.</summary>
    private static async Task<bool> AppliedAsync(World w, Op op) => op.Kind switch
    {
        Writer.RemoveRole => !await w.R.AssignmentExistsAsync(op.Target, Admin),
        Writer.DisableCredential or Writer.EntraPrimaryPolicy => await w.R.CredentialDisabledAsync(op.Target),
        Writer.RemoveLocalLink => await w.R.LinkCountAsync(op.Target, 1) == 0,
        Writer.Designate => await w.W.DesignatedAsync(op.Target),
        Writer.Revoke => !await w.W.DesignatedAsync(op.Target),
        Writer.BareReset => await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{op.Target}';") == 0,
        Writer.Switch => await w.Scratch.ScalarAsync<string>($"SELECT CredentialId FROM UserTotpCredentials WHERE UserId = N'{op.Target}';") == w.Candidate?.CandidateId,
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>The action and target of the row a committed <paramref name="op"/> writes.</summary>
    private static (string Action, string Target) SuccessRow(Op op) => op.Kind switch
    {
        Writer.RemoveRole => (AuditActionNames.RoleRemoved, op.Target),
        Writer.DisableCredential => (AuditActionNames.CredentialDisabled, op.Target),
        Writer.RemoveLocalLink => (AuditActionNames.IdentityLinkRemoved, "L" + op.Target),
        Writer.EntraPrimaryPolicy => (AuditActionNames.LocalDisabledEntraPrimary, op.Target),
        Writer.Designate => (AuditActionNames.RecoveryAccessDesignated, op.Target),
        Writer.Revoke => (AuditActionNames.RecoveryAccessRevoked, op.Target),
        Writer.BareReset => (AuditActionNames.MfaReset, op.Target),
        Writer.Switch => (AuditActionNames.MfaReplaced, op.Target),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>The action and target of the row a refused (or, for the policy, deferred) <paramref name="op"/> writes after its rollback.</summary>
    private static (string Action, string Target) RefusalRow(Op op) => op.Kind switch
    {
        Writer.RemoveRole => (AuditActionNames.BlockedLastPlatformAdminRemoval, op.Target),
        Writer.DisableCredential => (AuditActionNames.BlockedLastAdminCredentialDisabled, op.Target),
        Writer.RemoveLocalLink => (AuditActionNames.BlockedLastAdminLinkRemoved, "L" + op.Target),
        Writer.EntraPrimaryPolicy => (AuditActionNames.LocalDisableDeferredLastAdmin, op.Target),
        Writer.Designate or Writer.Revoke or Writer.BareReset or Writer.Switch => (AuditActionNames.AdministratorViabilityBlocked, op.Target),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static string RefusalOperation(Writer kind) => kind switch
    {
        Writer.Designate => "recovery_access.designate",
        Writer.Revoke => "recovery_access.revoke",
        Writer.BareReset => "mfa.reset",
        Writer.Switch => "mfa.replace",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool IsApplied(string outcome) =>
        outcome == Applied || outcome == "policy:" + EntraPrimaryLocalDisableOutcome.Disabled;

    private static readonly string PolicyDisabled = "policy:" + EntraPrimaryLocalDisableOutcome.Disabled;

    private static readonly string PolicyDeferredFallback = "policy:" + EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator;

    private static async Task ReachedAsync(PauseAt pause, Task operation)
    {
        var first = await Task.WhenAny(pause.Reached, operation, Task.Delay(Patience));
        if (first == operation)
            throw new Xunit.Sdk.XunitException($"The first writer finished without reaching the pause: {await (Task<string>)operation}.");
        if (first != pause.Reached)
            throw new TimeoutException("The first writer did not reach the pause.");
    }

    // ================================================================================================ the engines

    /// <summary>
    /// The first writer runs everything but its rows and its commit, holding the gate; the second is shown waiting for the application lock
    /// and for no row; then the first commits and the second is decided on what it committed.
    /// </summary>
    private static async Task<(string First, string Second)> ConcurrentlyAsync(World w, Op first, Op second)
    {
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var a = Task.Run(() => RunAsync(w, first, pause));
        await ReachedAsync(pause, a);
        (await ProbeGateAsync(w.Scratch)).Should().Be(-1, "the first writer holds the administrator-set gate while it is parked");

        var b = Task.Run(() => RunAsync(w, second));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(w.Scratch, 1, b);
        (await RowLockWaitersAsync(w.Scratch)).Should().Be(0, "the second writer waits for the application lock, not for a row: it has read and locked nothing");

        pause.Release();
        var firstOutcome = await a.WaitAsync(Patience);
        var secondOutcome = await b.WaitAsync(Patience);
        return (firstOutcome, secondOutcome);
    }

    private static async Task<(string First, string Second)> OneAfterTheOtherAsync(World w, Op first, Op second)
    {
        var firstOutcome = await RunAsync(w, first);
        var secondOutcome = await RunAsync(w, second);
        return (firstOutcome, secondOutcome);
    }

    private static Task<(string First, string Second)> PairAsync(World w, Op first, Op second, bool concurrently) =>
        concurrently ? ConcurrentlyAsync(w, first, second) : OneAfterTheOtherAsync(w, first, second);

    /// <summary>
    /// What a pair left: each writer's outcome, its effect in the catalog, and its row (a committed writer one success row and no refusal row;
    /// a refused or deferred one no effect, no success row and one refusal row; a committed pair one success row each).
    /// </summary>
    private static async Task AssertPairAsync(World w, Op first, Op second, (string First, string Second) outcomes, string expectedFirst, string expectedSecond)
    {
        outcomes.First.Should().Be(expectedFirst, "the first writer is allowed on its own");
        outcomes.Second.Should().Be(expectedSecond, "the second writer is decided on what the first committed");

        foreach (var (op, outcome) in new[] { (first, outcomes.First), (second, outcomes.Second) })
        {
            var applied = IsApplied(outcome);
            (await AppliedAsync(w, op)).Should().Be(applied, $"{op.Kind} {(applied ? "committed" : "was refused and rolled back")}");
            var success = SuccessRow(op);
            (await w.R.AuditRowsAsync(success.Action, success.Target)).Should().Be(applied ? 1 : 0, $"{op.Kind}: one success row only when it committed");
            var refusal = RefusalRow(op);
            (await w.R.AuditRowsAsync(refusal.Action, refusal.Target)).Should().Be(applied ? 0 : 1, $"{op.Kind}: one refusal row only when it was refused, written after the rollback");
            if (!applied && op.Kind is Writer.Designate or Writer.Revoke or Writer.BareReset or Writer.Switch)
            {
                var row = (await w.W.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, op.Target)).Should().ContainSingle().Which;
                row.Fields["operation"].Should().Be(RefusalOperation(op.Kind));
                row.Fields["code"].Should().Be(outcome["refused:".Length..]);
            }
        }
    }

    /// <summary>The viability state as the host reads it, in the configuration of the case.</summary>
    private static async Task<AdministratorViabilityState> ViabilityAsync(World w)
    {
        await using var db = w.Scratch.NewDb();
        return await AdministratorViabilitySqlServerTests.Service(db, HostReading, EntraRegistrationSqlHarness.SecretResolver(db)).ReadAsync();
    }

    // ================================================================================================ A10.2b.9: two reductions of the
    // Entra-independent set by administrators, while Recovery Access is not healthy

    public static IEnumerable<object[]> PersonPairs() =>
        from concurrently in new[] { true, false }
        from rcsi in new[] { false, true }
        from first in new[] { Writer.RemoveRole, Writer.DisableCredential, Writer.RemoveLocalLink }
        from second in new[] { Writer.RemoveRole, Writer.DisableCredential, Writer.RemoveLocalLink }
        select new object[] { first, second, concurrently, rcsi };

    [Theory]
    [MemberData(nameof(PersonPairs))]
    public async Task F1_two_reductions_of_the_last_two_entra_independent_administrators_commit_once_and_refuse_once(Writer first, Writer second, bool concurrently, bool rcsi)
    {
        // Recovery Access is designated but not healthy, and is never counted although it has the role and a Local credential. A and B are
        // the only Entra-independent administrators; each also signs in through Microsoft, and E signs in only through Microsoft, so I-VIABLE
        // never decides: only I-FALLBACK does. Either change alone leaves one Entra-independent administrator; both together leave none.
        var w = await WorldAsync(rcsi);
        var recovery = await UnhealthyRecoveryAsync(w);
        var a = await BothWaysAsync(w, "a");
        var b = await BothWaysAsync(w, "b");
        await EntraOnlyAsync(w, "e");
        var before = await ViabilityAsync(w);
        before.RecoveryHealthy.Should().BeFalse();
        before.Now.EntraIndependentAdministrators.Should().BeEquivalentTo([a, b], "Recovery Access is never counted");

        var (x, y) = (new Op(first, a), new Op(second, b));
        var outcomes = await PairAsync(w, x, y, concurrently);

        await AssertPairAsync(w, x, y, outcomes, Applied, FallbackRequired);
        var after = await ViabilityAsync(w);
        after.Now.EntraIndependentAdministrators.Should().BeEquivalentTo([b], "an administrator who signs in without Microsoft remains");
        after.Restart.EntraIndependentAdministrators.Should().BeEquivalentTo([b]);
        (await w.W.DesignatedAsync(recovery)).Should().BeTrue();
    }

    // ================================================================================================ A10.2b.9, D-2: a designation is a
    // reduction of the Entra-independent set too

    public static IEnumerable<object[]> DesignationPairs() =>
        from concurrently in new[] { true, false }
        from rcsi in new[] { false, true }
        from other in new[] { Writer.RemoveRole, Writer.DisableCredential, Writer.RemoveLocalLink, Writer.EntraPrimaryPolicy }
        from designationFirst in new[] { true, false }
        select new object[] { other, designationFirst, concurrently, rcsi };

    [Theory]
    [MemberData(nameof(DesignationPairs))]
    public async Task F2_a_designation_and_a_reduction_of_the_other_entra_independent_administrator_commit_once(Writer other, bool designationFirst, bool concurrently, bool rcsi)
    {
        // Nobody is designated. Designating A takes A out of both sets (Recovery Access is never counted) and leaves Recovery Access not
        // healthy (A has no authenticator); the other change takes B out of the Entra-independent set. Either alone leaves one Entra-independent
        // administrator; both together leave none while Recovery Access is not healthy. E keeps an administrator who can sign in.
        var w = await WorldAsync(rcsi);
        var a = await BothWaysAsync(w, "a");
        var b = await BothWaysAsync(w, "b");
        await EntraOnlyAsync(w, "e");

        var designate = new Op(Writer.Designate, a);
        var reduce = new Op(other, b);
        var (x, y) = designationFirst ? (designate, reduce) : (reduce, designate);
        var outcomes = await PairAsync(w, x, y, concurrently);

        var expectedFirst = x.Kind == Writer.EntraPrimaryPolicy ? "policy:" + EntraPrimaryLocalDisableOutcome.Disabled : Applied;
        var expectedSecond = y.Kind == Writer.EntraPrimaryPolicy
            ? "policy:" + EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator
            : FallbackRequired;
        await AssertPairAsync(w, x, y, outcomes, expectedFirst, expectedSecond);

        var remaining = designationFirst ? b : a;
        (await ViabilityAsync(w)).Now.EntraIndependentAdministrators.Should().BeEquivalentTo([remaining]);
        (await w.W.DesignatedCountAsync()).Should().Be(designationFirst ? 1 : 0);
    }
}
