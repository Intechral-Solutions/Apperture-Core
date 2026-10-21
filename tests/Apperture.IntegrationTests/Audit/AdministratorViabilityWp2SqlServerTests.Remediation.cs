using System.Data.Common;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP2 review remediation. M-1: a change that leaves every stored fact and the configuration as they were needs no trusted
// configuration, and a reduction aimed at an administrator still fails closed. M-2: a raise-only writer is held to its own effect on
// the user it changed, so a configuration reload, a recovery code consumed at sign-in or another administrator's change between its
// two reads cannot fail it, while a genuine reduction still does and rolls back.
public sealed partial class AdministratorViabilityWp2SqlServerTests
{
    // ------------------------------------------------------------------------------------- untrusted configurations

    public enum UntrustedConfiguration
    {
        /// <summary>The OpenID Connect handler's options cannot be read (Entra reading unknown).</summary>
        HandlerUnavailable,

        /// <summary>The running Entra options failed validation (Entra reading untrusted).</summary>
        InvalidEntraConfiguration,
    }

    private static DeploymentAuthenticationReading Untrusted(UntrustedConfiguration kind) => kind switch
    {
        UntrustedConfiguration.HandlerUnavailable => new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted,
            ConfigurationTrustLevel.Trusted,
            [],
            true,
            new EntraAuthOptions { ClientSecret = "deployment-secret" },
            AuthoritativeAboutSecrets: true,
            new RunningAuthenticationReading(
                new LocalAuthOptions { Enabled = true },
                new EntraAuthOptions { Enabled = true, Authority = "https://login.microsoftonline.com/" + Tenant, ClientId = "client", ClientSecret = "secret" },
                RunningHandlerBinding.Unavailable("InvalidOperationException"))),
        _ => new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted,
            ConfigurationTrustLevel.Untrusted,
            ["The running authentication options failed validation: Entra:ClientId is required."],
            true,
            new EntraAuthOptions(),
            AuthoritativeAboutSecrets: true,
            Running: null),
    };

    private static DeploymentAuthenticationReading LocalSignInOff() =>
        new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], false, new EntraAuthOptions(), AuthoritativeAboutSecrets: true, Running: null);

    public static IEnumerable<object[]> OrdinaryOpsUnderUntrustedConfigurations() =>
        from op in new[] { Op.DisableCredential, Op.RemoveLocalLink, Op.RemoveEntraLink }
        from kind in Enum.GetValues<UntrustedConfiguration>()
        from rcsi in new[] { false, true }
        select new object[] { op, kind, rcsi };

    public static IEnumerable<object[]> AdministratorOpsUnderUntrustedConfigurations() =>
        from op in Enum.GetValues<Op>()
        from kind in Enum.GetValues<UntrustedConfiguration>()
        from rcsi in new[] { false, true }
        select new object[] { op, kind, rcsi };

    /// <summary>A user who holds no administrator role, with the sign-in inputs the writer is about to change.</summary>
    private static async Task<string> OrdinaryTargetAsync(RoleAuthorityWorld world, Op op)
    {
        var id = await world.NewUserAsync("ordinary");
        switch (op)
        {
            case Op.DisableCredential:
                await world.AddCredentialAsync(id);
                break;
            case Op.RemoveLocalLink:
                await world.AddCredentialAsync(id);
                await world.AddEntraLinkAsync(id, Tenant, proven: false);
                break;
            case Op.RemoveEntraLink:
                await world.AddEntraLinkAsync(id, Tenant, proven: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, "An ordinary user is not the target of a role removal here.");
        }

        return id;
    }

    [Theory]
    [MemberData(nameof(OrdinaryOpsUnderUntrustedConfigurations))]
    public async Task M1_an_ordinary_users_credential_or_link_change_is_allowed_when_the_entra_configuration_cannot_be_trusted(Op op, UntrustedConfiguration kind, bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        var actor = await ActorAsync(world, "actor");
        var viable = await LocalAdminAsync(world, "viable");
        var ordinary = await OrdinaryTargetAsync(world, op);

        await using var db = world.Scratch.NewDb();
        await RunAsync(world, op, actor, ordinary, db, Untrusted(kind));

        (await AppliedAsync(world, op, ordinary)).Should().BeTrue("nothing an administrator-viability definition reads was touched");
        (await world.AuditRowsAsync(SuccessAction(op), AuditTarget(op, ordinary))).Should().Be(1);
        (await world.AuditRowsAsync(BlockedAction(op), AuditTarget(op, ordinary))).Should().Be(0, "a permitted change records no refusal");
        (await ViabilityAsync(world.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([viable]);
    }

    [Theory]
    [MemberData(nameof(AdministratorOpsUnderUntrustedConfigurations))]
    public async Task M1_a_reduction_aimed_at_an_administrator_still_fails_closed_when_the_configuration_cannot_be_trusted_and_no_local_administrator_remains(Op op, UntrustedConfiguration kind, bool rcsi)
    {
        // The administrator who remains signs in only through Microsoft (and Recovery Access is healthy), so with a trusted
        // reading every one of these would be allowed: it is the unreadable Microsoft reading, and no proven Local administrator,
        // that refuses them.
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        var actor = await ActorAsync(world, "actor");
        await EntraOnlyAdminAsync(world, "spare");
        await HealthyRecoveryAsync(world);
        var target = await TargetOfAsync(world, op, "target");
        var before = await StateAsync(world.Scratch);

        await using var db = world.Scratch.NewDb();
        var refusal = await RefusedAsync(() => RunAsync(world, op, actor, target, db, Untrusted(kind)));

        refusal.Code.Should().Be("administrator_viability_unavailable");
        (await StateAsync(world.Scratch)).Should().Be(before, "the provisional change was rolled back");
        (await world.AuditRowsAsync(BlockedAction(op), AuditTarget(op, target))).Should().Be(1);
        (await world.AuditRowsAsync(SuccessAction(op), AuditTarget(op, target))).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(AdministratorOpsUnderUntrustedConfigurations))]
    public async Task M1_a_reduction_aimed_at_an_administrator_is_allowed_when_the_configuration_cannot_be_trusted_but_an_ordinary_administrator_still_signs_in_locally(Op op, UntrustedConfiguration kind, bool rcsi)
    {
        // The change leaves an ordinary administrator with an enabled Local credential and link while Local sign-in is on, in both
        // views: both invariants hold without Microsoft, so what Microsoft can do is not needed to allow it.
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        var actor = await ActorAsync(world, "actor");
        var spare = await LocalAdminAsync(world, "spare");
        var target = await TargetOfAsync(world, op, "target");

        await using var db = world.Scratch.NewDb();
        await RunAsync(world, op, actor, target, db, Untrusted(kind));

        (await AppliedAsync(world, op, target)).Should().BeTrue();
        (await world.AuditRowsAsync(SuccessAction(op), AuditTarget(op, target))).Should().Be(1);
        (await world.AuditRowsAsync(BlockedAction(op), AuditTarget(op, target))).Should().Be(0, "a permitted change records no refusal");
        (await ViabilityAsync(world.Scratch)).Now.EntraIndependentAdministrators.Should().Contain(spare);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M1_removing_an_administrators_unproven_entra_link_changes_no_fact_a_definition_reads_so_it_needs_no_trusted_configuration(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        var actor = await ActorAsync(world, "actor");
        var admin = await world.NewUserAsync("admin", Admin);
        await world.AddEntraLinkAsync(admin, Tenant, proven: false);

        await using var db = world.Scratch.NewDb();
        await RunAsync(world, Op.RemoveEntraLink, actor, admin, db, Untrusted(UntrustedConfiguration.HandlerUnavailable));

        (await world.LinkCountAsync(admin, 0)).Should().Be(0);
        (await world.AssignmentExistsAsync(admin, Admin)).Should().BeTrue();
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminLinkRemoved, "E" + admin)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M1_the_no_op_and_authorization_order_is_unchanged_under_an_untrusted_configuration(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var ordinary = await world.NewUserAsync("ordinary");
        await world.AddCredentialAsync(ordinary, disabled: true);
        var before = await StateAsync(world.Scratch);

        await using (var db = world.Scratch.NewDb())
            await RunAsync(world, Op.DisableCredential, actor, ordinary, db, Untrusted(UntrustedConfiguration.InvalidEntraConfiguration));

        (await StateAsync(world.Scratch)).Should().Be(before);
        (await world.AuditRowsAsync(AuditActionNames.CredentialDisabled, ordinary)).Should().Be(0);

        // An unauthorized caller is stopped before any of it.
        var nobody = await world.NewUserAsync("nobody");
        await using var db2 = world.Scratch.NewDb();
        var service = world.Service(db2, nobody, advisoryAllowsAll: false, deployment: Untrusted(UntrustedConfiguration.InvalidEntraConfiguration));
        var act = () => service.SetLocalCredentialDisabledAsync(new UserId(ordinary), disabled: false).AsTask();
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>
    /// The decision is made on the facts read under the gate in the writer's own transaction. A target who was an ordinary user when
    /// the request arrived but became an administrator before the writer got the gate is judged as the administrator he is.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task M1_unchanged_facts_are_determined_from_the_state_the_writer_reads_under_the_gate_not_from_an_earlier_read(bool becomesAdministrator, bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var scratch = world.Scratch;
        var actorA = await ActorAsync(world, "actor-a");
        var actorB = await ActorAsync(world, "actor-b");

        // The administrator who remains signs in only through Microsoft, which the reading cannot establish: if the user is judged
        // as the administrator he is, disabling his credential leaves no proven Local administrator and is refused.
        await EntraOnlyAdminAsync(world, "spare");
        await HealthyRecoveryAsync(world);
        var user = await world.NewUserAsync("user");
        await world.AddCredentialAsync(user);
        var other = await world.NewUserAsync("other");
        var promoted = becomesAdministrator ? user : other;

        // The first writer gives somebody the administrator role and is parked with the gate held, before its commit.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var a = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            await Service(world, db, actorA).AssignRoleAsync(new UserId(promoted), new RoleId(Admin));
        });
        await pause.Reached.WaitAsync(Patience);

        // The second writer arrives while the user is still an ordinary user, and waits for the gate.
        await using var dbB = scratch.NewDb();
        var b = Task.Run(() => RunAsync(world, Op.DisableCredential, actorB, user, dbB, Untrusted(UntrustedConfiguration.HandlerUnavailable)));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(scratch, 1, b);

        pause.Release();
        await a.WaitAsync(Patience);

        if (becomesAdministrator)
        {
            (await RefusedAsync(() => b.WaitAsync(Patience))).Code.Should().Be("administrator_viability_unavailable");
            (await world.CredentialDisabledAsync(user)).Should().BeFalse("the user is an administrator by the time the writer decides, which an earlier read would not have shown");
            (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminCredentialDisabled, user)).Should().Be(1);
        }
        else
        {
            await b.WaitAsync(Patience);
            (await world.CredentialDisabledAsync(user)).Should().BeTrue("an ordinary user's change needs no trusted configuration");
            (await world.AuditRowsAsync(AuditActionNames.CredentialDisabled, user)).Should().Be(1);
        }
    }

    // ------------------------------------------------------------------------------------------------- M-2

    /// <summary>The first read of the deployment's configuration returns one reading, every later read returns the other.</summary>
    private sealed class FlippingDeployment(DeploymentAuthenticationReading first, DeploymentAuthenticationReading then) : IDeploymentAuthenticationConfiguration
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public DeploymentAuthenticationReading Read() => Interlocked.Increment(ref _reads) == 1 ? first : then;
    }

    private static AdministratorViabilityCoordinator FlippingCoordinator(
        AppertureDbContext db, DeploymentAuthenticationReading first, DeploymentAuthenticationReading then, out FlippingDeployment deployment)
    {
        deployment = new FlippingDeployment(first, then);
        return new AdministratorViabilityCoordinator(
            new SqlPlatformAdministratorSetGate(db),
            new AdministratorViabilityService(
                new SqlAdministratorFactsReader(db),
                new EffectiveSignInConfigurationProvider(deployment, new SqlEntraInstanceRegistrationRepository(db))));
    }

    /// <summary>
    /// Arms once a statement satisfying <c>after</c> has run (the writer's own change), then pauses before the next statement that satisfies
    /// <c>pauseAt</c> (the start of the state read after it), so that something can happen between the two reads of one unit.
    /// </summary>
    private sealed class PauseOnNthStatement(Func<string, bool> after, Func<string, bool> pauseAt) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _seen;

        public Task Reached => _reached.Task;

        public void Release() => _release.TrySetResult();

        private async Task PauseIfAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var text = command.CommandText;
            if (Volatile.Read(ref _seen) == 0)
            {
                if (after(text))
                    Volatile.Write(ref _seen, 1);
                return;
            }

            if (!pauseAt(text) || Interlocked.Exchange(ref _seen, 2) == 2)
                return;
            _reached.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, cancellationToken);
            return result;
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M2_assigning_the_administrator_role_succeeds_when_the_last_recovery_code_is_consumed_between_the_reads(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var scratch = world.Scratch;
        var actor = await ActorAsync(world, "actor");
        var recovery = await HealthyRecoveryAsync(world);
        var newcomer = await world.NewUserAsync("newcomer");
        (await ViabilityAsync(scratch)).RecoveryHealthy.Should().BeTrue();

        // Parked between the state before and the state after: the second read of the persisted registration begins the second read.
        var pause = new PauseOnNthStatement(
            after: sql => sql.Contains("INSERT INTO [UserRoles]", StringComparison.Ordinal),
            pauseAt: sql => sql.Contains("[EntraInstanceRegistrations]", StringComparison.Ordinal));
        var assign = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            await Service(world, db, actor).AssignRoleAsync(new UserId(newcomer), new RoleId(Admin));
        });
        await pause.Reached.WaitAsync(Patience);

        // A legitimate Recovery Access sign-in consumes the last unused code (ruling C-14): no gate, non-refusable.
        await scratch.ExecuteAsync($"UPDATE UserMfaRecoveryCodes SET ConsumedAtUtc = SYSDATETIMEOFFSET() WHERE UserId = N'{recovery}';");
        pause.Release();
        await assign.WaitAsync(Patience);

        (await world.AssignmentExistsAsync(newcomer, Admin)).Should().BeTrue("the assignment cannot have caused the exhausted codes");
        (await world.AuditRowsAsync(AuditActionNames.RoleAssigned, newcomer)).Should().Be(1);
        (await ViabilityAsync(scratch)).RecoveryHealthy.Should().BeFalse("the consumption did happen between the reads");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M2_re_enabling_a_credential_succeeds_when_the_configuration_is_reloaded_between_the_reads(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var viable = await LocalAdminAsync(world, "viable");
        var stranded = await StrandedAdminAsync(world, "stranded");
        var before = await ViabilityAsync(world.Scratch);
        before.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([viable]);

        await using var db = world.Scratch.NewDb();
        var coordinator = FlippingCoordinator(db, AdministratorViabilitySqlServerTests.Reading(null), LocalSignInOff(), out var deployment);
        await Service(world, db, actor, coordinator: coordinator).SetLocalCredentialDisabledAsync(new UserId(stranded), disabled: false);

        deployment.Reads.Should().Be(2, "the configuration was read before the change and after it, and was different the second time");
        (await world.CredentialDisabledAsync(stranded)).Should().BeFalse();
        (await world.AuditRowsAsync(AuditActionNames.LocalCredentialReenabled, stranded)).Should().Be(1);
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminCredentialDisabled, stranded)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M2_adding_a_link_succeeds_when_the_authentication_environment_changes_between_the_reads(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        var actor = await ActorAsync(world, "actor");

        // The affected user signs in only through a proven Entra link for the accepted tenant (their Local credential is disabled) and
        // has no Local link yet. The writer adds the Local link; it can only add.
        var target = await EntraOnlyAdminAsync(world, "target");
        await world.Scratch.ExecuteAsync($"DELETE FROM IdentityLinks WHERE UserId = N'{target}' AND Source = 1;");

        // R-L1: the environment change must really change what Microsoft sign-in is, in both views, and the test says so. A
        // reading that merely replaced the running options with an estimate left the persisted registration, and the deployment
        // secret, usable: Entra stayed usable in the Restart view and the case proved nothing.
        var entraOn = await EffectiveAsync(world.Scratch, EntraReading);
        var entraOff = await EffectiveAsync(world.Scratch, EntraTurnedOffEverywhere());
        (entraOn.Now.EntraSignInUsable, entraOn.Restart.EntraSignInUsable).Should().Be((true, true), "before the change Microsoft sign-in works now and after a restart");
        (entraOff.Now.EntraSignInUsable, entraOff.Restart.EntraSignInUsable).Should().Be((false, false), "after it Microsoft sign-in works in neither view");
        (await ViabilityAsync(world.Scratch, EntraReading)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([target], "the target is viable under the first environment");
        var offState = await ViabilityAsync(world.Scratch, EntraTurnedOffEverywhere());
        offState.Now.ViableOrdinaryAdministrators.Should().BeEmpty("and is not viable under the second, in the Now view");
        offState.Restart.ViableOrdinaryAdministrators.Should().BeEmpty("or in the Restart view: a comparison of the whole system would read this as a reduction the writer caused");

        // The environment changes between the writer's two reads, which the writer did not do and cannot undo.
        await using var db = world.Scratch.NewDb();
        var coordinator = FlippingCoordinator(db, EntraReading, EntraTurnedOffEverywhere(), out var deployment);
        await Service(world, db, actor, coordinator: coordinator).AddIdentityLinkAsync(new UserId(target), IdentitySource.Local, null);

        deployment.Reads.Should().Be(2, "the configuration was read before the change and after it, and was different the second time");
        (await world.LinkCountAsync(target, 1)).Should().Be(1);
        (await world.AuditRowsAsync(AuditActionNames.IdentityLinkAdded, await world.Scratch.ScalarAsync<string>($"SELECT Id FROM IdentityLinks WHERE UserId = N'{target}' AND Source = 1;"))).Should().Be(1);
    }

    /// <summary>
    /// A host whose Microsoft sign-in is off in the running options and whose persisted registration is unusable after a restart too:
    /// the deployment has no client secret for the registration to use. (An estimate from a reader that cannot see the running host
    /// would leave the registration, and the deployment secret, usable, and change nothing.)
    /// </summary>
    private static DeploymentAuthenticationReading EntraTurnedOffEverywhere() =>
        new(
            ConfigurationTrustLevel.Trusted,
            ConfigurationTrustLevel.Trusted,
            [],
            true,
            new EntraAuthOptions(),
            AuthoritativeAboutSecrets: true,
            new RunningAuthenticationReading(new LocalAuthOptions { Enabled = true }, new EntraAuthOptions { Enabled = false }, RunningHandlerBinding.Disabled()));

    private static async Task<EffectiveSignInConfiguration> EffectiveAsync(AuditSqlScratch scratch, DeploymentAuthenticationReading reading)
    {
        await using var db = scratch.NewDb();
        return await new EffectiveSignInConfigurationProvider(new FlippingDeployment(reading, reading), new SqlEntraInstanceRegistrationRepository(db)).ReadAsync();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M2_a_target_who_could_not_sign_in_before_and_still_cannot_is_not_a_reduction(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        await LocalAdminAsync(world, "spare");
        var target = await StrandedAdminAsync(world, "target");
        await world.Scratch.ExecuteAsync($"DELETE FROM IdentityLinks WHERE UserId = N'{target}';");

        // The credential is disabled and stays so; a Local link is added. Nothing is raised and nothing is lost.
        await using var db = world.Scratch.NewDb();
        await Service(world, db, actor).AddIdentityLinkAsync(new UserId(target), IdentitySource.Local, null);

        (await world.LinkCountAsync(target, 1)).Should().Be(1);
        (await ViabilityAsync(world.Scratch)).Now.ViableOrdinaryAdministrators.Should().NotContain(target);
    }

    public static IEnumerable<object[]> UncertainConfigurationsOnBothModes() =>
        from kind in new[] { "local-unknown", "handler-unavailable", "invalid-entra" }
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    [Theory]
    [MemberData(nameof(UncertainConfigurationsOnBothModes))]
    public async Task M2_configuration_uncertainty_does_not_prevent_a_valid_raise(string kind, bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        await LocalAdminAsync(world, "spare");
        var newcomer = await world.NewUserAsync("newcomer");
        var linkless = await world.NewUserAsync("linkless", Admin);
        await world.Scratch.ExecuteAsync($"DELETE FROM IdentityLinks WHERE UserId = N'{linkless}';");
        var reading = kind switch
        {
            "local-unknown" => LocalSettingUnknown(),
            "handler-unavailable" => Untrusted(UntrustedConfiguration.HandlerUnavailable),
            _ => Untrusted(UntrustedConfiguration.InvalidEntraConfiguration),
        };

        await using (var db = world.Scratch.NewDb())
            await Service(world, db, actor, reading).AssignRoleAsync(new UserId(newcomer), new RoleId(Admin));
        await using (var db = world.Scratch.NewDb())
            await Service(world, db, actor, reading).AddIdentityLinkAsync(new UserId(linkless), IdentitySource.Local, null);

        (await world.AssignmentExistsAsync(newcomer, Admin)).Should().BeTrue();
        (await world.LinkCountAsync(linkless, 1)).Should().Be(1);
    }

    /// <summary>After the link insert is saved, takes the user's proven Entra link away inside the same transaction: a writer that has a defect.</summary>
    private sealed class RemoveEntraLinkAfterFirstSave(string userId) : SaveChangesInterceptor
    {
        private int _done;

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _done, 1) == 0 && eventData.Context is { } context)
            {
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = $"DELETE FROM IdentityLinks WHERE UserId = N'{userId}' AND Source = 0;";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M2_a_genuine_reduction_of_the_affected_user_still_fails_and_rolls_back_inside_the_audited_transaction(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        var actor = await ActorAsync(world, "actor");
        await LocalAdminAsync(world, "spare");
        var target = await EntraOnlyAdminAsync(world, "target");
        await world.Scratch.ExecuteAsync($"DELETE FROM IdentityLinks WHERE UserId = N'{target}' AND Source = 1;");
        (await ViabilityAsync(world.Scratch, EntraReading)).Now.ViableOrdinaryAdministrators.Should().Contain(target);
        var before = await StateAsync(world.Scratch);

        // Adding a Local link can only add; this writer, as injected, also removes the proven Entra link the user could sign in with.
        await using var db = world.Scratch.NewDb(new RemoveEntraLinkAfterFirstSave(target));
        var act = () => Service(world, db, actor, EntraReading).AddIdentityLinkAsync(new UserId(target), IdentitySource.Local, null).AsTask();

        (await act.Should().ThrowAsync<AuditInvariantException>()).Which.Message.Should().Contain("reduced it");
        (await StateAsync(world.Scratch)).Should().Be(before, "the unit rolled the defective change back");
        (await world.AuditRowsAsync(AuditActionNames.IdentityLinkAdded, "L" + target)).Should().Be(0);
    }
}
