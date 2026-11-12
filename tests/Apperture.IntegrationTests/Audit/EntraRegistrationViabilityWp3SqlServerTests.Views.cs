using System.Data.Common;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP3: a change to the registration is judged in the running host's view (Now) and in the view of the persisted
// configuration after a restart (Restart), and a change to the sign-in policy, which only moves the chooser, cannot move either.
public sealed partial class EntraRegistrationViabilityWp3SqlServerTests
{
    // ================================================================================================ Now and Restart

    /// <summary>
    /// The host started when the registration for <see cref="Tenant"/> was active, so its handler is bound to that tenant. The
    /// administrator <c>A</c> is proven for it. The administrator <c>B</c> is proven only for <see cref="OtherTenant"/>, the tenant of
    /// the inactive second registration.
    /// </summary>
    private async Task<(Scenario S, string B)> RunningHostAsync(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, reading: Running(Tenant), withSecondRegistration: true);
        var b = await EntraOnlyAdminAsync(s.World, "other-tenant", OtherTenant);
        return (s, b);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task D4_a_switch_that_is_fine_after_a_restart_is_refused_because_the_running_handler_would_accept_nobody(bool rcsi)
    {
        var (s, b) = await RunningHostAsync(rcsi);
        var state = await ViabilityAsync(s.Scratch, s.Reading);
        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb();
        var response = await Tenants(s, db).Activate(s.R2!, CancellationToken.None);

        // After the switch and a restart B would sign in (Restart is fine), but until then the handler still sends everybody through
        // the old directory while the sign-in checks tokens against the new one: nobody is accepted now.
        response.Should().BeOfType<ConflictObjectResult>();
        CodeOf(response).Should().Be("last_viable_sign_in_method");
        (await StateAsync(s.Scratch)).Should().Be(before);
        (await RefusalDetailsAsync(s.Scratch, s.R2!)).Should().Equal("activate/sign_in_lockout");
        b.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task D4_an_already_inconsistent_running_view_does_not_stop_the_repairing_switch_back(bool rcsi)
    {
        var (s, b) = await RunningHostAsync(rcsi);
        // The persisted active registration has been switched to the second tenant (by a deployment tool, say) while the host keeps
        // running with the handler it started with: the Now view accepts nobody, the Restart view accepts B.
        await s.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET IsActive = CASE WHEN Id = N'{s.R2}' THEN 1 ELSE 0 END;");
        var inconsistent = await ViabilityAsync(s.Scratch, s.Reading);
        inconsistent.Now.ViableOrdinaryAdministrators.Should().BeEmpty("the handler is bound to a directory the sign-in no longer accepts");
        inconsistent.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([b]);

        await using var db = s.Scratch.NewDb();
        var response = await Tenants(s, db).Activate(s.R1, CancellationToken.None);

        response.Should().BeOfType<NoContentResult>("switching back only raises the Now view and keeps the Restart view viable");
        var repaired = await ViabilityAsync(s.Scratch, s.Reading);
        repaired.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
        repaired.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task D4_after_a_simulated_restart_the_running_view_follows_the_persisted_registration_and_the_two_views_agree(bool rcsi)
    {
        var (s, b) = await RunningHostAsync(rcsi);
        await s.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET IsActive = CASE WHEN Id = N'{s.R2}' THEN 1 ELSE 0 END;");

        var running = await ViabilityAsync(s.Scratch, Running(Tenant));
        running.Now.ViableOrdinaryAdministrators.Should().BeEmpty();
        running.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([b]);

        // The host restarts: it now builds its handler for the persisted registration.
        var restarted = await ViabilityAsync(s.Scratch, Running(OtherTenant));
        restarted.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([b]);
        restarted.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([b]);
        restarted.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo(restarted.Restart.ViableOrdinaryAdministrators);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task D4_a_change_that_is_fine_now_but_not_after_a_restart_is_refused(bool rcsi)
    {
        // The host started with Microsoft sign-in off; the persisted registration is usable after a restart and A is proven for it.
        // Deleting that registration leaves A able to sign in now (nobody can, since the host runs without Entra) but not after a
        // restart; the Restart view alone decides this one.
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        var offNowOnAfterRestart = new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted,
            ConfigurationTrustLevel.Trusted,
            [],
            true,
            new EntraAuthOptions { ClientSecret = "deployment-secret" },
            AuthoritativeAboutSecrets: true,
            new RunningAuthenticationReading(new LocalAuthOptions { Enabled = true }, new EntraAuthOptions { Enabled = false }, RunningHandlerBinding.Disabled()));
        var scenario = new Scenario { World = s.World, Actor = s.Actor, A = s.A, R1 = s.R1, R2 = s.R2, Reading = offNowOnAfterRestart };
        var state = await ViabilityAsync(s.Scratch, offNowOnAfterRestart);
        state.Now.ViableOrdinaryAdministrators.Should().BeEmpty("the running host has Microsoft sign-in off");
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);

        // Nobody is viable now, so I-VIABLE holds vacuously in the Now view; the Restart view is what the delete would reduce.
        await using var db = s.Scratch.NewDb();
        var response = await Tenants(scenario, db).Delete(s.R1, CancellationToken.None);

        CodeOf(response).Should().Be("last_viable_sign_in_method");
        (await ViabilityAsync(s.Scratch, offNowOnAfterRestart)).Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A], "the refused delete was rolled back");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task D3_a_registration_with_no_azure_tenant_id_is_matched_by_the_tenant_of_its_authority(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        await s.World.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET AzureTenantId = NULL WHERE Id = N'{s.R1}';");
        (await ViabilityAsync(s.Scratch)).Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A], "the tenant is the one in the authority");

        await using var db = s.Scratch.NewDb();
        var item = await ItemAsync(s, s.R1);
        var response = (await Tenants(s, db).Patch(
            s.R1,
            Update(item, b => { b.Authority = $"https://login.microsoftonline.com/{OtherTenant}/v2.0"; b.AzureTenantId = null; }),
            CancellationToken.None)).Result!;

        CodeOf(response).Should().Be("last_viable_sign_in_method");
    }

    // ================================================================================================ sign-in policy

    private static EntraAuthOptions UsableEntra() => new()
    {
        Enabled = true,
        Authority = "https://login.microsoftonline.com/" + Tenant,
        ClientId = "client",
        ClientSecret = "secret",
        AzureTenantId = Tenant,
    };

    private static ConfigAuthUiPolicyController Policy(Scenario s, AppertureDbContext db, IPlatformAdministratorSetGate? gate = null, EntraAuthOptions? entra = null,
        IAuthorizationEvaluationService? authorization = null, Apperture.Core.Application.Audit.IAuditedUnitOfWork? unit = null)
    {
        var controller = new ConfigAuthUiPolicyController(
            new SqlInstanceAuthUiPolicyRepository(db),
            s.Principal,
            authorization ?? new EntraRegistrationSqlHarness.AllowAll(),
            Microsoft.Extensions.Options.Options.Create(new LocalAuthOptions { Enabled = true }),
            Microsoft.Extensions.Options.Options.Create(entra ?? UsableEntra()),
            unit ?? AuditRecorderTestSupport.NewUnit(db, s.Principal),
            EntraRegistrationSqlHarness.Viability(db, reading: s.Reading, resolver: EntraRegistrationSqlHarness.SecretResolver(db), gate: gate));
        controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        return controller;
    }

    private static async Task<AuthUiPolicyResponse> CurrentPolicyAsync(Scenario s)
    {
        await using var db = s.Scratch.NewDb();
        return (AuthUiPolicyResponse)((OkObjectResult)(await Policy(s, db).Get(CancellationToken.None)).Result!).Value!;
    }

    private static UpdateAuthUiPolicyRequestBody PolicyBody(AuthUiPolicyResponse current, bool? showLocal = null, bool? preferEntra = null, bool? autoRedirect = null) =>
        new()
        {
            ShowLocalSignInInPrimaryChooser = showLocal ?? current.ShowLocalSignInInPrimaryChooser,
            PreferEntraInChooser = preferEntra ?? current.PreferEntraInChooser,
            AutoRedirectToEntra = autoRedirect ?? current.AutoRedirectToEntra,
            RowVersionBase64 = current.RowVersionBase64,
        };

    private static string Sets(AdministratorViabilityState state) =>
        string.Join(
            ";",
            new[] { ViabilityView.Now, ViabilityView.Restart }.Select(view =>
                $"{view}:voa={string.Join(",", state.For(view).ViableOrdinaryAdministrators.Order(StringComparer.Ordinal))}:ei={string.Join(",", state.For(view).EntraIndependentAdministrators.Order(StringComparer.Ordinal))}"));

    public static IEnumerable<object[]> PolicyChangesOnBothModes() =>
        from flags in new[] { (false, true, false), (false, false, true), (true, true, true), (false, true, true) }
        from rcsi in new[] { false, true }
        select new object[] { flags.Item1, flags.Item2, flags.Item3, rcsi };

    [Theory]
    [MemberData(nameof(PolicyChangesOnBothModes))]
    public async Task A7_a_chooser_only_policy_change_cannot_move_either_set_and_the_local_route_stays_usable_when_the_chooser_hides_it(
        bool showLocal, bool preferEntra, bool autoRedirect, bool rcsi)
    {
        var s = await WithLocalAdministratorAsync(rcsi);
        var before = await ViabilityAsync(s.Scratch);
        var local = (await s.Scratch.ScalarAsync<string>("SELECT TOP 1 UserId FROM LocalCredentials WHERE IsDisabled = 0 AND UserId IN (SELECT UserId FROM UserRoles WHERE RoleId = N'" + Admin + "' ) AND UserId NOT IN (SELECT Id FROM Users WHERE IsBreakGlassEligible = 1);"))!;
        var current = await CurrentPolicyAsync(s);

        await using (var db = s.Scratch.NewDb())
            (await Policy(s, db).Patch(PolicyBody(current, showLocal, preferEntra, autoRedirect), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();

        var stored = await CurrentPolicyAsync(s);
        stored.ShowLocalSignInInPrimaryChooser.Should().Be(showLocal);
        var after = await ViabilityAsync(s.Scratch);
        Sets(after).Should().Be(Sets(before), "the chooser flags are not an input of administrator viability");
        after.Now.EntraIndependentAdministrators.Should().Contain(local, "the direct Local route is available whenever Local sign-in is enabled, whatever the chooser shows");
        after.RecoveryHealthy.Should().Be(before.RecoveryHealthy);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_policy_change_keeps_its_contract_400_for_an_unsatisfiable_policy_409_for_a_stale_row_version_and_403_without_permission(bool rcsi)
    {
        var s = await WithLocalAdministratorAsync(rcsi);
        var current = await CurrentPolicyAsync(s);
        var before = await StateAsync(s.Scratch);

        await using (var db = s.Scratch.NewDb())
            (await Policy(s, db, entra: new EntraAuthOptions()).Patch(PolicyBody(current, showLocal: false), CancellationToken.None)).Result
                .Should().BeOfType<BadRequestObjectResult>("hiding Local needs a usable Entra: the request validation is unchanged");
        await using (var db = s.Scratch.NewDb())
            (await Policy(s, db).Patch(new UpdateAuthUiPolicyRequestBody { RowVersionBase64 = null }, CancellationToken.None)).Result
                .Should().BeOfType<BadRequestObjectResult>();
        await using (var db = s.Scratch.NewDb())
            (await Policy(s, db, authorization: new EntraRegistrationSqlHarness.Allow()).Patch(PolicyBody(current, preferEntra: true), CancellationToken.None)).Result
                .Should().BeOfType<ForbidResult>();

        await using (var db = s.Scratch.NewDb())
            (await Policy(s, db).Patch(PolicyBody(current, preferEntra: true), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        await using (var db = s.Scratch.NewDb())
            (await Policy(s, db).Patch(PolicyBody(current, preferEntra: false), CancellationToken.None)).Result
                .Should().BeOfType<ConflictObjectResult>("the row version the client holds is stale");

        (await StateAsync(s.Scratch)).Should().NotBe(before);
        var rows = await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.SignInPolicyUpdated}' AND TargetId = N'{AuditEvents.SignInPolicyId}';");
        rows.Should().BeGreaterThanOrEqualTo(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_policy_no_op_writes_no_row_and_a_busy_gate_is_the_only_new_outcome(bool rcsi)
    {
        var s = await WithLocalAdministratorAsync(rcsi);
        var current = await CurrentPolicyAsync(s);
        var auditBefore = await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using (var db = s.Scratch.NewDb())
            (await Policy(s, db).Patch(PolicyBody(current), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>("the same flags answer as before");
        (await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore);

        await using var holder = await RawSession.BeginAsync(s.Scratch.ConnectionString);
        await holder.ExecuteAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'" + PlatformAdministratorSetLock.ResourceName + "', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;");
        await using var busyDb = s.Scratch.NewDb();
        var busy = (await Policy(s, busyDb, new SqlPlatformAdministratorSetGate(busyDb, TimeSpan.FromMilliseconds(300))).Patch(PolicyBody(current, preferEntra: true), CancellationToken.None)).Result!;

        busy.Should().BeOfType<ConflictObjectResult>();
        CodeOf(busy).Should().Be("governance_operation_busy");
        (await CurrentPolicyAsync(s)).PreferEntraInChooser.Should().Be(current.PreferEntraInChooser, "nothing changed while the gate was held");
        await holder.RollbackAsync();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A14_the_policy_writer_takes_the_gate_once_after_the_operation_lock_and_before_it_reads_or_writes_the_policy(bool rcsi)
    {
        var s = await WithLocalAdministratorAsync(rcsi);
        var current = await CurrentPolicyAsync(s);
        var log = new AdministratorViabilityWp2SqlServerTests.OrderedLog();

        await using var db = s.Scratch.NewDb(new AdministratorViabilityWp2SqlServerTests.LoggingStatements(log));
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
        (await Policy(s, db, gate).Patch(PolicyBody(current, preferEntra: true), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();

        var entries = log.Entries.ToList();
        var gateAt = entries.IndexOf(AdministratorViabilityWp2SqlServerTests.GateEntry);
        entries.Count(e => e == AdministratorViabilityWp2SqlServerTests.GateEntry).Should().Be(1);
        entries.Take(gateAt).Should().NotContain(e => e.Contains("InstanceAuthUiPolicies", StringComparison.Ordinal), "the policy row is not read before the gate");
        entries.Skip(gateAt).Should().Contain(e => e.Contains("InstanceAuthUiPolicies", StringComparison.Ordinal));
        entries.Skip(gateAt).Should().NotContain(e => e.Contains("[UserRoles]", StringComparison.Ordinal), "the policy writer reads no administrator state");
    }

    // ================================================================================================ lock order

    public enum Unit
    {
        Create,
        Edit,
        Delete,
        Activate,
    }

    public static IEnumerable<object[]> EveryUnitOnBothModes() =>
        from unit in Enum.GetValues<Unit>()
        from rcsi in new[] { false, true }
        select new object[] { unit, rcsi };

    private static bool LocksOrWrites(string statement) =>
        statement.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase)
        || statement.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
        || statement.Contains("UPDATE [", StringComparison.OrdinalIgnoreCase)
        || statement.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase)
        || statement.Contains("MERGE [", StringComparison.OrdinalIgnoreCase);

    private static bool TouchesRegistrationsOrSecrets(string statement) =>
        statement.Contains("EntraInstanceRegistrations", StringComparison.Ordinal)
        || statement.Contains("EntraRegistrationDiagnostics", StringComparison.Ordinal)
        || statement.Contains("SecretDescriptors", StringComparison.Ordinal)
        || statement.Contains("SecretVersions", StringComparison.Ordinal);

    /// <summary>The real gate, noting the application locks the connection holds just before and just after it is taken.</summary>
    private sealed class ProbingGate(IPlatformAdministratorSetGate inner, AdministratorViabilityWp2SqlServerTests.OrderedLog log, AppertureDbContext db) : IPlatformAdministratorSetGate
    {
        public int HeldBefore { get; private set; }
        public int HeldAfter { get; private set; }

        public async ValueTask AcquireAsync(CancellationToken cancellationToken = default)
        {
            log.Add(AdministratorViabilityWp2SqlServerTests.GateEntry);
            HeldBefore = await HeldAsync(cancellationToken);
            await inner.AcquireAsync(cancellationToken);
            HeldAfter = await HeldAsync(cancellationToken);
        }

        private async Task<int> HeldAsync(CancellationToken cancellationToken)
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'GRANT' AND request_session_id = @@SPID;";
            return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
    }

    [Theory]
    [MemberData(nameof(EveryUnitOnBothModes))]
    public async Task A14_the_gate_is_the_first_lock_after_the_operation_lock_before_any_registration_or_secret_row_is_read_locked_or_written(Unit unit, bool rcsi)
    {
        var s = await WithLocalAdministratorAsync(rcsi);
        var log = new AdministratorViabilityWp2SqlServerTests.OrderedLog();
        var secretBody = EntraRegistrationSqlHarness.CreateBody("Ordered");
        secretBody.ClientSecret = "ordered-secret";
        var item = await ItemAsync(s, s.R1);

        await using var db = s.Scratch.NewDb(new AdministratorViabilityWp2SqlServerTests.LoggingStatements(log));
        var gate = new ProbingGate(new SqlPlatformAdministratorSetGate(db), log, db);
        var controller = Tenants(s, db, gate);
        log.Add("<<start>>"); // a marker after the construction of the controller and the item read
        switch (unit)
        {
            case Unit.Create:
                (await controller.Create(secretBody, CancellationToken.None)).Result.Should().BeOfType<CreatedAtActionResult>();
                break;
            case Unit.Edit:
                (await controller.Patch(s.R1, Update(item, b => { b.DisplayName = "Edited"; b.ClientSecret = "edited-secret"; }), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
                break;
            case Unit.Delete:
                (await controller.Delete(s.R2!, CancellationToken.None)).Should().BeOfType<NoContentResult>();
                break;
            default:
                (await controller.Activate(s.R2!, CancellationToken.None)).Should().BeOfType<NoContentResult>();
                break;
        }

        var entries = log.Entries.ToList();
        var outside = log.OutsideTransaction.ToHashSet();
        var gateAt = entries.IndexOf(AdministratorViabilityWp2SqlServerTests.GateEntry);
        gateAt.Should().BeGreaterThan(entries.IndexOf("<<start>>"));
        entries.Count(e => e == AdministratorViabilityWp2SqlServerTests.GateEntry).Should().Be(1, "one gate acquisition per attempt");
        gate.HeldBefore.Should().Be(1, "the operation lock is the only application lock held when the gate is requested");
        gate.HeldAfter.Should().Be(2, "the gate is the second application lock");

        var insideBeforeGate = entries.Select((e, i) => (e, i)).Where(x => x.i < gateAt && !outside.Contains(x.i)).Select(x => x.e).ToList();
        insideBeforeGate.Should().NotContain(e => LocksOrWrites(e) || TouchesRegistrationsOrSecrets(e),
            "no registration, secret or other row may be read, locked or written inside the unit before the gate");
        entries.Skip(gateAt).Should().Contain(e => TouchesRegistrationsOrSecrets(e) && LocksOrWrites(e), "the unit does write after the gate; the order is what is being checked");
        if (unit is Unit.Create or Unit.Edit)
            entries.Skip(gateAt).Should().Contain(e => e.Contains("SecretDescriptors", StringComparison.Ordinal) && LocksOrWrites(e), "a supplied secret is stored after the gate");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_running_options_are_built_before_the_unit_opens_so_their_first_build_cannot_read_behind_its_locks(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        var log = new AdministratorViabilityWp2SqlServerTests.OrderedLog();
        var tracing = new TracingDeployment(new EntraRegistrationSqlHarness.FixedDeployment(s.Reading), log);

        await using var db = s.Scratch.NewDb(new AdministratorViabilityWp2SqlServerTests.LoggingStatements(log));
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
        await Tenants(s, db, gate, deployment: tracing).Delete(s.R2!, CancellationToken.None);

        var entries = log.Entries.ToList();
        var prepared = entries.IndexOf(PreparedEntry);
        var gateAt = entries.IndexOf(AdministratorViabilityWp2SqlServerTests.GateEntry);
        prepared.Should().BeGreaterThanOrEqualTo(0);
        gateAt.Should().BeGreaterThan(prepared, "the options were built before the unit took its first lock after the operation lock, so before it held anything");
        log.OutsideTransaction.Should().NotContain(i => i > prepared, "nothing the unit does is outside its transaction once the options are built");
        entries.Take(prepared).Should().NotContain(e => LocksOrWrites(e), "no write or lock has happened by the time the options are built");
    }

    private const string PreparedEntry = "<<options prepared>>";

    private sealed class TracingDeployment(IDeploymentAuthenticationConfiguration inner, AdministratorViabilityWp2SqlServerTests.OrderedLog log) : IDeploymentAuthenticationConfiguration
    {
        public DeploymentAuthenticationReading Read() => inner.Read();

        public void EnsureInitialized()
        {
            log.Add(PreparedEntry);
            inner.EnsureInitialized();
        }
    }
}
