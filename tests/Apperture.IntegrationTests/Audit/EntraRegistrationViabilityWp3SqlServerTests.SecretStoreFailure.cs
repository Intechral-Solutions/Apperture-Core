using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP3 review, M-1 and L-1, against real SQL Server and on both isolation modes.
//
// M-1: a secret store that keeps failing (an unresolved DPAPI key, reference or provider) leaves the Microsoft reading unknown. A
// change whose own result still holds an ordinary administrator who signs in locally (an enabled credential and link, Local sign-in on,
// in both views) satisfies both invariants without Microsoft, so it commits normally, with no refusal row; everything else fails
// closed exactly as before, and refusals roll back with their secrets.
//
// L-1: a database failure (a deadlock victim, a timeout) while the viability evaluation resolves a secret inside the unit is the
// unit's failure: the unit rolls back and reruns, takes the gate again, decides again, and writes no business refusal. The
// exceptions below are real SqlExceptions captured from the server, not built by hand.
public sealed partial class EntraRegistrationViabilityWp3SqlServerTests
{
    private const string Unavailable = "administrator_viability_unavailable";

    public enum SafeOp
    {
        /// <summary>Replace the active registration's stored client secret.</summary>
        ReplaceSecret,

        /// <summary>Edit the active registration so that it is disabled.</summary>
        Disable,

        /// <summary>Delete the active registration (and retire its secret).</summary>
        Delete,

        /// <summary>Make the second registration (another tenant) the active one.</summary>
        Switch,

        /// <summary>Remove the Platform Administrator role from a compromised administrator.</summary>
        RemoveAdministratorRole,
    }

    public static IEnumerable<object[]> SafeOpsOnBothModes() =>
        from op in Enum.GetValues<SafeOp>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    public static IEnumerable<object[]> ReducingSafeOpsOnBothModes() =>
        from op in Enum.GetValues<SafeOp>().Where(o => o != SafeOp.ReplaceSecret)
        from healthyRecovery in new[] { false, true }
        from rcsi in new[] { false, true }
        select new object[] { op, healthyRecovery, rcsi };

    private sealed record SafeWorld(Scenario S, string AdminActor, IReadOnlyList<string> LocalAdmins, string SecretRef1, string SecretRef2)
    {
        /// <summary>The administrator whose role <see cref="SafeOp.RemoveAdministratorRole"/> removes.</summary>
        public string Compromised => LocalAdmins[0];
    }

    private static async Task<string> StrandedActorAsync(RoleAuthorityWorld world, string stem)
    {
        var id = await world.NewUserAsync(stem, Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{id}';");
        return id;
    }

    /// <summary>
    /// The standard scenario with a secret store that cannot be read: the reliant administrator signs in through the active
    /// registration, which names a stored secret of its own, and so does the second one. <paramref name="localAdmins"/> ordinary
    /// administrators can sign in locally. Recovery Access is not healthy unless asked for, so it cannot stand in.
    /// </summary>
    private async Task<SafeWorld> SafeWorldAsync(
        bool rcsi,
        int localAdmins,
        bool healthyRecovery = false,
        DeploymentAuthenticationReading? reading = null,
        ISecretResolver? resolver = null)
    {
        var s = await ReliantAsync(rcsi, reading: reading, resolver: resolver ?? new OutageResolver(), withSecondRegistration: true, recoveryHealthy: healthyRecovery);
        var secret1 = await StoreSecretAsync(s.World, s.R1, "first-secret");
        await s.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secret1}' WHERE Id = N'{s.R1}';");
        var secret2 = await StoreSecretAsync(s.World, s.R2!, "second-secret");
        await s.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secret2}' WHERE Id = N'{s.R2}';");

        var locals = new List<string>();
        for (var i = 0; i < localAdmins; i++)
            locals.Add(await LocalAdminAsync(s.World, "local" + i));
        var adminActor = await StrandedActorAsync(s.World, "adminactor");
        return new SafeWorld(s, adminActor, locals, secret1, secret2);
    }

    /// <summary>Runs one operation; null when it succeeded, else the stable code of the refusal.</summary>
    private static async Task<string?> RunSafeOpAsync(SafeWorld w, SafeOp op, AppertureDbContext db)
    {
        var s = w.S;
        if (op == SafeOp.RemoveAdministratorRole)
        {
            var service = s.World.Service(
                db,
                w.AdminActor,
                advisoryAllowsAll: true,
                strongAuthentication: new AllowingStrongAuthentication(),
                coordinator: EntraRegistrationSqlHarness.Viability(db, reading: s.Reading, resolver: s.Resolver));
            try
            {
                await service.RemoveRoleAsync(new UserId(w.Compromised), new RoleId(Admin));
                return null;
            }
            catch (GovernanceConflictException refused)
            {
                return refused.Code;
            }
        }

        var controller = Tenants(s, db);
        IActionResult result;
        switch (op)
        {
            case SafeOp.ReplaceSecret:
            {
                var item = await ItemAsync(s, s.R1);
                result = (await controller.Patch(s.R1, Update(item, b => b.ClientSecret = "replacement-secret"), CancellationToken.None)).Result!;
                break;
            }

            case SafeOp.Disable:
            {
                var item = await ItemAsync(s, s.R1);
                result = (await controller.Patch(s.R1, Update(item, b => b.Enabled = false), CancellationToken.None)).Result!;
                break;
            }

            case SafeOp.Delete:
                result = await controller.Delete(s.R1, CancellationToken.None);
                break;

            default:
                result = await controller.Activate(s.R2!, CancellationToken.None);
                break;
        }

        return Succeeded(result) ? null : CodeOf(result) ?? result.GetType().Name;
    }

    private static string TargetOf(SafeWorld w, SafeOp op) => op switch
    {
        SafeOp.RemoveAdministratorRole => w.Compromised,
        SafeOp.Switch => w.S.R2!,
        _ => w.S.R1,
    };

    private static async Task<int> SuccessRowsAsync(SafeWorld w, SafeOp op) => op switch
    {
        SafeOp.RemoveAdministratorRole => await w.S.World.AuditRowsAsync(AuditActionNames.RoleRemoved, w.Compromised),
        SafeOp.Delete => await RowsAsync(w.S.Scratch, AuditActionNames.TenantRegistrationDeleted, w.S.R1),
        SafeOp.Switch => await RowsAsync(w.S.Scratch, AuditActionNames.TenantRegistrationActivated, w.S.R2!),
        _ => await RowsAsync(w.S.Scratch, AuditActionNames.TenantRegistrationUpdated, w.S.R1),
    };

    private async Task AssertEntraUnknownAsync(SafeWorld w) =>
        (await ViabilityAsync(w.S.Scratch, w.S.Reading, w.S.Resolver)).Trust.Entra.Should().Be(
            ConfigurationTrustLevel.Unknown, "the case is only meaningful while the secret store cannot be read");

    // ================================================================================================ M-1: allowed

    [Theory]
    [MemberData(nameof(SafeOpsOnBothModes))]
    public async Task M1_a_persistent_secret_store_failure_does_not_stop_a_change_while_an_ordinary_administrator_still_signs_in_locally(SafeOp op, bool rcsi)
    {
        // Two administrators can sign in locally: the one the change targets (for the role removal) and another who remains.
        var w = await SafeWorldAsync(rcsi, localAdmins: 2);
        await AssertEntraUnknownAsync(w);
        var s = w.S;
        var descriptorsBefore = await SecretDescriptorsAsync(s.Scratch);

        await using var db = s.Scratch.NewDb();
        var outcome = await RunSafeOpAsync(w, op, db);

        outcome.Should().BeNull("the result still holds a proven Entra-independent administrator, so Microsoft is not needed to allow it");
        (await SuccessRowsAsync(w, op)).Should().Be(1);
        (await RefusalRowsAsync(s.Scratch, TargetOf(w, op))).Should().Be(0, "no false viability refusal is recorded");
        (await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.BlockedLastPlatformAdminRemoval}' AND TargetId = N'{w.Compromised}';"))
            .Should().Be(0);

        switch (op)
        {
            case SafeOp.ReplaceSecret:
                (await SecretDescriptorsAsync(s.Scratch)).Should().Be(descriptorsBefore + 1, "the new secret was stored");
                (await s.Scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{w.SecretRef1}';")).Should().Be((int)SecretState.Disabled, "the replaced secret was retired with it");
                (await s.Scratch.ScalarAsync<string>($"SELECT ClientSecretRef FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';")).Should().NotBe(w.SecretRef1);
                (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationClientSecretStored, s.R1)).Should().Be(1);
                (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationClientSecretRetired, s.R1)).Should().Be(1);
                break;

            case SafeOp.Disable:
                (await s.Scratch.ScalarAsync<bool>($"SELECT Enabled FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';")).Should().BeFalse();
                (await s.Scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{w.SecretRef1}';")).Should().Be((int)SecretState.Active, "a disabled registration keeps its secret");
                break;

            case SafeOp.Delete:
                (await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';")).Should().Be(0);
                (await s.Scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{w.SecretRef1}';")).Should().Be((int)SecretState.Disabled, "the deleted registration's secret was retired with it");
                (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationClientSecretRetired, s.R1)).Should().Be(1);
                break;

            case SafeOp.Switch:
                (await s.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{s.R2}';")).Should().BeTrue();
                (await s.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';")).Should().BeFalse();
                break;

            default:
                (await s.World.AssignmentExistsAsync(w.Compromised, Admin)).Should().BeFalse();
                (await s.World.AssignmentExistsAsync(w.LocalAdmins[1], Admin)).Should().BeTrue("the administrator who signs in locally remains");
                break;
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M1_a_replaced_secret_under_a_persistent_failure_is_still_stored_and_retired_atomically_with_a_refusal_that_rolls_both_back(bool rcsi)
    {
        // No administrator signs in locally, so disabling the registration is refused; the replacement secret and the retirement of
        // the old one roll back with it, as when the store is healthy (M6).
        var w = await SafeWorldAsync(rcsi, localAdmins: 0);
        await AssertEntraUnknownAsync(w);
        var s = w.S;
        var item = await ItemAsync(s, s.R1);
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb();
        var response = (await Tenants(s, db).Patch(
            s.R1, Update(item, b => { b.Enabled = false; b.ClientSecret = "replacement-secret"; }), CancellationToken.None)).Result!;

        CodeOf(response).Should().Be(Unavailable);
        (await StateAsync(s.Scratch)).Should().Be(before, "the registration, the new secret and the retirement rolled back together");
        (await s.Scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{w.SecretRef1}';")).Should().Be((int)SecretState.Active);
        (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationClientSecretStored, s.R1)).Should().Be(0);
        (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationClientSecretRetired, s.R1)).Should().Be(0);
    }

    // ================================================================================================ M-1: still refused

    [Theory]
    [MemberData(nameof(ReducingSafeOpsOnBothModes))]
    public async Task M1_without_an_ordinary_administrator_who_signs_in_locally_the_change_still_fails_closed_and_recovery_access_does_not_stand_in(
        SafeOp op, bool healthyRecovery, bool rcsi)
    {
        // The role removal targets the only Local administrator; the others have no Local administrator at all.
        var w = await SafeWorldAsync(rcsi, localAdmins: op == SafeOp.RemoveAdministratorRole ? 1 : 0, healthyRecovery: healthyRecovery);
        await AssertEntraUnknownAsync(w);
        var s = w.S;
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb();
        var outcome = await RunSafeOpAsync(w, op, db);

        outcome.Should().Be(Unavailable);
        (await StateAsync(s.Scratch)).Should().Be(before, "the refusal rolled everything back");
        (await SuccessRowsAsync(w, op)).Should().Be(0);
        if (op != SafeOp.RemoveAdministratorRole)
            (await RefusalRowsAsync(s.Scratch, TargetOf(w, op))).Should().Be(1, "a refusal is recorded once with the operation's existing action");
    }

    public enum TrustBreak
    {
        /// <summary>The Local setting could not be established.</summary>
        LocalConfigurationUnknown,

        /// <summary>Local sign-in is switched off, so a Local credential is no way in.</summary>
        LocalSignInDisabled,

        /// <summary>The administrator's Local credential is disabled.</summary>
        CredentialDisabled,

        /// <summary>The administrator has a credential but no Local identity link.</summary>
        LocalLinkMissing,
    }

    public static IEnumerable<object[]> TrustBreaksOnBothModes() =>
        from broken in Enum.GetValues<TrustBreak>()
        from rcsi in new[] { false, true }
        select new object[] { broken, rcsi };

    [Theory]
    [MemberData(nameof(TrustBreaksOnBothModes))]
    public async Task M1_a_local_fallback_that_is_not_established_never_passes_the_proof(TrustBreak broken, bool rcsi)
    {
        var reading = broken switch
        {
            TrustBreak.LocalConfigurationUnknown => new DeploymentAuthenticationReading(
                ConfigurationTrustLevel.Unknown,
                ConfigurationTrustLevel.Trusted,
                ["LocalAuth:Enabled is not set in the configuration this reader was started with."],
                true,
                new EntraAuthOptions { ClientSecret = "deployment-secret" },
                AuthoritativeAboutSecrets: true,
                Running: null),
            TrustBreak.LocalSignInDisabled => Deploy(local: false),
            _ => Deploy(),
        };
        var w = await SafeWorldAsync(rcsi, localAdmins: 1, reading: reading);
        var local = w.LocalAdmins[0];
        if (broken == TrustBreak.CredentialDisabled)
            await w.S.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{local}';");
        if (broken == TrustBreak.LocalLinkMissing)
            await w.S.Scratch.ExecuteAsync($"DELETE FROM IdentityLinks WHERE UserId = N'{local}' AND Source = 1;");
        var s = w.S;
        var before = await StateAsync(s.Scratch);

        // A switch, not a disable: with Local sign-in off, disabling the only registration leaves no sign-in path at all, which the
        // instance-level rule (a 400) refuses first, and this case is about the viability decision.
        await using var db = s.Scratch.NewDb();
        var outcome = await RunSafeOpAsync(w, SafeOp.Switch, db);

        outcome.Should().Be(Unavailable, $"{broken}: no administrator is demonstrably able to sign in without Microsoft");
        (await StateAsync(s.Scratch)).Should().Be(before);
        (await RefusalRowsAsync(s.Scratch, s.R2!)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M1_the_proof_follows_the_final_state_a_change_that_removes_the_last_local_administrator_is_refused_even_though_one_exists_before(bool rcsi)
    {
        var w = await SafeWorldAsync(rcsi, localAdmins: 1);
        await AssertEntraUnknownAsync(w);
        var before = await StateAsync(w.S.Scratch);

        await using var db = w.S.Scratch.NewDb();
        var outcome = await RunSafeOpAsync(w, SafeOp.RemoveAdministratorRole, db);

        outcome.Should().Be(Unavailable);
        (await StateAsync(w.S.Scratch)).Should().Be(before);
        (await w.S.World.AssignmentExistsAsync(w.Compromised, Admin)).Should().BeTrue();
    }

    // ================================================================================================ L-1

    public enum DatabaseFault
    {
        /// <summary>SQL Server error 1205: chosen as a deadlock victim.</summary>
        Deadlock,

        /// <summary>SQL Server error -2: the command timed out.</summary>
        Timeout,
    }

    public static IEnumerable<object[]> DatabaseFaultsOnBothModes() =>
        from fault in Enum.GetValues<DatabaseFault>()
        from rcsi in new[] { false, true }
        select new object[] { fault, rcsi };

    /// <summary>A real deadlock victim exception: two sessions deadlock on two rows and the lower priority one is chosen.</summary>
    private static async Task<SqlException> CaptureDeadlockAsync(AuditSqlScratch scratch)
    {
        var table = "##deadlock_" + Guid.NewGuid().ToString("N");
        await using var anchor = new SqlConnection(scratch.ConnectionString);
        await anchor.OpenAsync();
        await using (var create = anchor.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {table} (Id int NOT NULL PRIMARY KEY, V int NOT NULL); INSERT INTO {table} VALUES (1, 0), (2, 0);";
            await create.ExecuteNonQueryAsync();
        }

        await using var winner = await RawSession.BeginAsync(scratch.ConnectionString, highDeadlockPriority: true);
        await using var victim = await RawSession.BeginAsync(scratch.ConnectionString);
        await winner.ExecuteAsync($"UPDATE {table} SET V = 1 WHERE Id = 1;");
        await victim.ExecuteAsync($"UPDATE {table} SET V = 1 WHERE Id = 2;");
        var winnerSecond = Task.Run(() => winner.ExecuteAsync($"UPDATE {table} SET V = 2 WHERE Id = 2;"));

        var deadline = DateTime.UtcNow.Add(Patience);
        while (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {victim.SessionId};") == 0)
        {
            if (DateTime.UtcNow > deadline || winnerSecond.IsCompleted)
                throw new Xunit.Sdk.XunitException("The winning session never waited on the victim's row.");
            await Task.Delay(25);
        }

        SqlException? caught = null;
        try
        {
            await victim.ExecuteAsync($"UPDATE {table} SET V = 2 WHERE Id = 1;");
        }
        catch (SqlException deadlock)
        {
            caught = deadlock;
        }

        await winnerSecond.WaitAsync(Patience);
        await winner.CommitAsync();
        caught.Should().NotBeNull("the second session closes the cycle and the server picks a victim");
        caught!.Number.Should().Be(1205);
        return caught;
    }

    /// <summary>A real command timeout exception.</summary>
    private static async Task<SqlException> CaptureTimeoutAsync(AuditSqlScratch scratch)
    {
        await using var connection = new SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "WAITFOR DELAY '00:00:10';";
        command.CommandTimeout = 1;
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException timeout)
        {
            timeout.Number.Should().Be(-2);
            return timeout;
        }

        throw new Xunit.Sdk.XunitException("The command did not time out.");
    }

    private static Task<SqlException> CaptureAsync(DatabaseFault kind, AuditSqlScratch scratch) =>
        kind == DatabaseFault.Deadlock ? CaptureDeadlockAsync(scratch) : CaptureTimeoutAsync(scratch);

    /// <summary>The real resolver, except that its first <c>faults</c> calls fail as given: the secret read inside the unit failing.</summary>
    private sealed class FaultingResolver(ISecretResolver inner, Exception fault, int faults) : ISecretResolver
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _calls) <= faults ? throw fault : inner.ResolveAsync(secretRef, cancellationToken);

        public ValueTask<ResolvedSecret> ResolveOwnedAsync(SecretRef secretRef, SecretOwner owner, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _calls) <= faults ? throw fault : inner.ResolveOwnedAsync(secretRef, owner, cancellationToken);
    }

    /// <summary>
    /// The reliant administrator signs in through the active registration, which uses the deployment secret. A second registration
    /// for the same tenant holds a stored secret of its own: activating it keeps the administrator viable, but only if that secret
    /// can be resolved after the change. No other administrator can sign in, so an unknown reading would refuse.
    /// </summary>
    private async Task<Scenario> SwitchToStoredSecretScenarioAsync(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var a = await EntraOnlyAdminAsync(world, "reliant");
        var actor = await world.NewUserAsync("actor");
        var r1 = await SeedAsync(world, Tenant, active: true);
        var r2 = await SeedAsync(world, Tenant, active: false);
        var secret2 = await StoreSecretAsync(world, r2, "second-secret");
        await world.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secret2}' WHERE Id = N'{r2}';");
        return new Scenario { World = world, Actor = actor, A = a, R1 = r1, R2 = r2, Reading = Deploy() };
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task L1_a_deadlock_victim_resolving_the_secret_inside_the_unit_reruns_the_unit_takes_the_gate_again_and_records_no_refusal(bool rcsi)
    {
        var kind = DatabaseFault.Deadlock;
        var s = await SwitchToStoredSecretScenarioAsync(rcsi);
        var fault = await CaptureAsync(kind, s.Scratch);
        var attempts = new AttemptCounter();

        await using var db = s.Scratch.NewDb(attempts);
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), new AdministratorViabilityWp2SqlServerTests.OrderedLog());
        var resolver = new FaultingResolver(EntraRegistrationSqlHarness.SecretResolver(db), fault, faults: 1);
        var controller = EntraRegistrationSqlHarness.Controller(
            s.Scratch,
            db,
            s.Principal,
            viability: EntraRegistrationSqlHarness.Viability(db, reading: s.Reading, resolver: resolver, gate: gate),
            store: EntraRegistrationSqlHarness.SecretStore(db));

        var response = await controller.Activate(s.R2!, CancellationToken.None);

        Succeeded(response).Should().BeTrue($"the {kind} was the unit's failure, so the unit reran and decided on a secret it could read; got {response.GetType().Name} {CodeOf(response)}");
        resolver.Calls.Should().Be(2, "the failed read, then the read of the rerun");
        attempts.Started.Should().Be(2, "one attempt failed with the database error and the execution strategy reran it");
        gate.Acquired.Should().Be(2, "a rerun is a new transaction: the gate is taken again and the viability is decided again");
        (await s.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{s.R2}';")).Should().BeTrue();
        (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationActivated, s.R2!)).Should().Be(1);
        (await RefusalRowsAsync(s.Scratch, s.R2!)).Should().Be(0, "a database failure is never a business refusal");
        (await RefusalRowsAsync(s.Scratch, s.R1)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task L1_a_command_timeout_resolving_the_secret_inside_the_unit_is_the_units_failure_as_any_other_timeout_in_it_is_not_retried_and_not_a_refusal(bool rcsi)
    {
        // The execution strategy does not retry a command timeout, so the unit's contract for it is: roll back, and fail the call.
        var s = await SwitchToStoredSecretScenarioAsync(rcsi);
        var fault = await CaptureAsync(DatabaseFault.Timeout, s.Scratch);
        var attempts = new AttemptCounter();
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb(attempts);
        var resolver = new FaultingResolver(EntraRegistrationSqlHarness.SecretResolver(db), fault, faults: 1);
        var controller = EntraRegistrationSqlHarness.Controller(
            s.Scratch,
            db,
            s.Principal,
            viability: EntraRegistrationSqlHarness.Viability(db, reading: s.Reading, resolver: resolver),
            store: EntraRegistrationSqlHarness.SecretStore(db));

        var act = () => controller.Activate(s.R2!, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().Match<Exception>(e => IsOrWraps(e, fault), "the timeout itself reaches the caller, not a viability refusal");
        attempts.Started.Should().Be(1);
        (await StateAsync(s.Scratch)).Should().Be(before, "the unit rolled back the activation");
        (await RefusalRowsAsync(s.Scratch, s.R2!)).Should().Be(0);
        (await RefusalRowsAsync(s.Scratch, s.R1)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(DatabaseFaultsOnBothModes))]
    public async Task L1_a_database_failure_that_keeps_failing_is_the_units_failure_not_a_refusal_and_changes_nothing(DatabaseFault kind, bool rcsi)
    {
        var s = await SwitchToStoredSecretScenarioAsync(rcsi);
        var fault = await CaptureAsync(kind, s.Scratch);
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb();
        var resolver = new FaultingResolver(EntraRegistrationSqlHarness.SecretResolver(db), fault, faults: int.MaxValue);
        var controller = EntraRegistrationSqlHarness.Controller(
            s.Scratch,
            db,
            s.Principal,
            viability: EntraRegistrationSqlHarness.Viability(db, reading: s.Reading, resolver: resolver),
            store: EntraRegistrationSqlHarness.SecretStore(db));

        var act = () => controller.Activate(s.R2!, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().Match<Exception>(e => IsOrWraps(e, fault), "the database error itself reaches the caller, not a viability refusal");
        (await StateAsync(s.Scratch)).Should().Be(before);
        (await RefusalRowsAsync(s.Scratch, s.R2!)).Should().Be(0);
        (await RefusalRowsAsync(s.Scratch, s.R1)).Should().Be(0);
    }

    private static bool IsOrWraps(Exception exception, Exception expected)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (ReferenceEquals(current, expected) || (current is SqlException sql && expected is SqlException wanted && sql.Number == wanted.Number))
                return true;
        }

        return false;
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task L1_a_secret_store_that_cannot_be_reached_is_still_an_unknown_reading_a_refusal_and_never_a_rerun(bool rcsi)
    {
        var s = await SwitchToStoredSecretScenarioAsync(rcsi);
        var attempts = new AttemptCounter();
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb(attempts);
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), new AdministratorViabilityWp2SqlServerTests.OrderedLog());
        var controller = EntraRegistrationSqlHarness.Controller(
            s.Scratch,
            db,
            s.Principal,
            viability: EntraRegistrationSqlHarness.Viability(db, reading: s.Reading, resolver: new OutageResolver(), gate: gate),
            store: EntraRegistrationSqlHarness.SecretStore(db));

        var response = await controller.Activate(s.R2!, CancellationToken.None);

        CodeOf(response).Should().Be(Unavailable, "an unreachable external store says nothing about the secret");
        attempts.Started.Should().Be(1, "this is a business refusal, not a transactional failure");
        gate.Acquired.Should().Be(1);
        (await StateAsync(s.Scratch)).Should().Be(before);
        (await RefusalRowsAsync(s.Scratch, s.R2!)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task L1_a_confirmed_missing_secret_is_still_a_known_absence_with_a_database_that_is_healthy(bool rcsi)
    {
        // The second registration is the active one, names a stored secret the store has retired, and does not use the deployment secret.
        var s = await SwitchToStoredSecretScenarioAsync(rcsi);
        await s.Scratch.ExecuteAsync($"""
            UPDATE SecretDescriptors SET State = {(int)SecretState.Disabled} WHERE Id = (SELECT ClientSecretRef FROM EntraInstanceRegistrations WHERE Id = N'{s.R2}');
            UPDATE EntraInstanceRegistrations SET IsActive = 0 WHERE Id = N'{s.R1}';
            UPDATE EntraInstanceRegistrations SET IsActive = 1, UsesDeploymentSecret = 0 WHERE Id = N'{s.R2}';
            """);

        var state = await ViabilityAsync(s.Scratch, NoDeploymentSecret(), null);

        state.Trust.Entra.Should().Be(ConfigurationTrustLevel.Trusted, "the store answered: there is no such secret");
        state.Restart.ViableOrdinaryAdministrators.Should().BeEmpty("without a secret Microsoft sign-in is known to be unusable");
    }
}
