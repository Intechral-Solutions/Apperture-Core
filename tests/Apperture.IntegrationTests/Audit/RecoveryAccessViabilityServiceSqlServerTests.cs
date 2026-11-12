using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP4B-C, Recovery Access writers against real SQL Server on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT: the designation
/// and the strict revocation as reducers of administrator viability, provisioning as a gated raiser, and the administrators' password
/// change (L-P3), each through the production service as the emergency command line composes it (the real gate, facts, attested
/// configuration reader and attestation store, audited unit and refusal writer). What a case concludes is read back with SQL of its own.
/// Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class RecoveryAccessViabilityServiceSqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly ActorId Cli = new(EmergencyAccessService.CliActorId);
    private const string NewPassword = "quiet-maple-lantern-harbor";

    private readonly AuditIsolationCatalogs _catalogs;

    public RecoveryAccessViabilityServiceSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    /// <summary>A failing case must not leave an operation parked with the gate.</summary>
    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    public static IEnumerable<object[]> BothModesAndOrders() =>
        from rcsi in new[] { false, true }
        from designationFirst in new[] { true, false }
        select new object[] { rcsi, designationFirst };

    public static IEnumerable<object[]> BothModesAndMissingEvidence() =>
        from rcsi in new[] { false, true }
        from evidence in new[] { "missing", "stale", "stopped" }
        select new object[] { rcsi, evidence };

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

    private static async Task MakeEvidenceUnavailableAsync(ServiceViabilityWorld w, string evidence)
    {
        switch (evidence)
        {
            case "missing":
                return;
            case "stale":
                await w.AttestAsync(ServiceViabilityWorld.AtStart());
                await w.AgeAttestationAsync();
                return;
            case "stopped":
                await w.AttestAsync(ServiceViabilityWorld.AtStart());
                await w.StopHostAsync();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(evidence));
        }
    }

    // ================================================================================================ designation

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_that_keeps_an_entra_independent_administrator_commits_with_the_attested_evidence_it_used(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var target = await w.EntraIndependentAdminAsync("target");
        await w.EntraIndependentAdminAsync("other");
        await w.AttestAsync(ServiceViabilityWorld.AtStart());

        await using (var db = w.Scratch.NewDb())
            await w.Emergency(db).DesignateAsync(new UserId(target), Cli);

        (await w.DesignatedAsync(target)).Should().BeTrue();
        var row = (await w.RowsAsync(AuditActionNames.RecoveryAccessDesignated, target)).Should().ContainSingle().Which;
        row.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested,
            "the result was proven safe on the attested Local reading alone: another administrator signs in without Microsoft");
        (await w.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, target)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_that_would_leave_no_viable_ordinary_administrator_is_refused_rolled_back_and_recorded(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var only = await w.EntraIndependentAdminAsync("only");
        await w.AttestAsync(ServiceViabilityWorld.Established(), registration: true);

        Exception? refusal;
        await using (var db = w.Scratch.NewDb())
            refusal = await Record.ExceptionAsync(() => w.Emergency(db).DesignateAsync(new UserId(only), Cli).AsTask());

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.LastViableAdministrator);
        (await w.DesignatedAsync(only)).Should().BeFalse("the designation, its rename and its rows were rolled back");
        (await w.Scratch.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{only}';")).Should().NotBe(EmergencyAccessIdentity.LocalUsername);
        (await w.RowsAsync(AuditActionNames.RecoveryAccessDesignated, only)).Should().BeEmpty();
        var row = (await w.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, only)).Should().ContainSingle().Which;
        row.Outcome.Should().Be(AuditOutcomeCodes.Rejected);
        row.Actor.Should().Be(ActorLiterals.EmergencyCli);
        row.Fields["operation"].Should().Be("recovery_access.designate");
        row.Fields["code"].Should().Be(GovernanceConflictCodes.LastViableAdministrator);
        row.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested);
    }

    [Theory]
    [MemberData(nameof(BothModesAndMissingEvidence))]
    public async Task A_designation_without_a_fresh_attestation_is_refused_as_unavailable_and_changes_nothing(bool rcsi, string evidence)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var target = await w.EntraIndependentAdminAsync("target");
        await w.EntraIndependentAdminAsync("other");
        await MakeEvidenceUnavailableAsync(w, evidence);

        Exception? refusal;
        await using (var db = w.Scratch.NewDb())
            refusal = await Record.ExceptionAsync(() => w.Emergency(db).DesignateAsync(new UserId(target), Cli).AsTask());

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AdministratorViabilityUnavailable);
        (await w.DesignatedAsync(target)).Should().BeFalse();
        var row = (await w.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, target)).Should().ContainSingle().Which;
        row.Fields["code"].Should().Be(GovernanceConflictCodes.AdministratorViabilityUnavailable);
        row.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Unavailable, $"the attestation was {evidence}");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_named_with_a_differently_cased_id_designates_and_records_the_stored_account(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var target = await w.EntraIndependentAdminAsync("target");
        await w.EntraIndependentAdminAsync("other");
        await w.AttestAsync(ServiceViabilityWorld.AtStart());
        var spelled = target.ToUpperInvariant();
        spelled.Should().NotBe(target, "the generated id has letters, so the spelling differs from the stored id");

        await using (var db = w.Scratch.NewDb())
            await w.Emergency(db).DesignateAsync(new UserId(spelled), Cli);

        (await w.DesignatedAsync(target)).Should().BeTrue();
        (await w.RowsAsync(AuditActionNames.RecoveryAccessDesignated, target)).Should().ContainSingle("the row names the stored account, exactly");
        (await w.RowsAsync(AuditActionNames.RecoveryAccessDesignated, spelled)).Should().BeEmpty("never the caller's spelling of it");
        (await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE TargetId COLLATE Latin1_General_BIN2 = N'{spelled}';")).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_refused_designation_named_with_a_differently_cased_id_records_the_stored_account(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var only = await w.EntraIndependentAdminAsync("only");
        await w.AttestAsync(ServiceViabilityWorld.Established(), registration: true);

        await using (var db = w.Scratch.NewDb())
            (await Record.ExceptionAsync(() => w.Emergency(db).DesignateAsync(new UserId(only.ToUpperInvariant()), Cli).AsTask()))
                .Should().BeOfType<GovernanceConflictException>();

        (await w.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, only)).Should().ContainSingle();
        (await w.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, only.ToUpperInvariant())).Should().BeEmpty();
    }

    // ================================================================================================ strict revocation

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_strict_revocation_with_another_entra_independent_administrator_commits_with_the_attested_evidence_it_used(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var recovery = await w.HealthyRecoveryAsync();
        await w.EntraIndependentAdminAsync("other");
        await w.AttestAsync(ServiceViabilityWorld.AtStart());

        await using (var db = w.Scratch.NewDb())
            await w.Emergency(db).RevokeAsync(confirmFallback: true, Cli);

        (await w.DesignatedCountAsync()).Should().Be(0);
        var row = (await w.RowsAsync(AuditActionNames.RecoveryAccessRevoked, recovery)).Should().ContainSingle().Which;
        row.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested,
            "revocation needs the Local reading and never Microsoft's");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_strict_revocation_without_another_entra_independent_administrator_is_refused_even_with_recovery_access_healthy(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var recovery = await w.HealthyRecoveryAsync();
        await w.EntraOnlyAdminAsync("entra");
        await w.AttestAsync(ServiceViabilityWorld.Established(), registration: true);

        Exception? refusal;
        await using (var db = w.Scratch.NewDb())
            refusal = await Record.ExceptionAsync(() => w.Emergency(db).RevokeAsync(confirmFallback: true, Cli).AsTask());

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        (await w.DesignatedAsync(recovery)).Should().BeTrue("the refused revocation was rolled back");
        (await w.RowsAsync(AuditActionNames.RecoveryAccessRevoked, recovery)).Should().BeEmpty();
        var row = (await w.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, recovery)).Should().ContainSingle().Which;
        row.Fields["operation"].Should().Be("recovery_access.revoke");
        row.Fields["code"].Should().Be(GovernanceConflictCodes.EntraIndependentFallbackRequired);
        row.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Attested);
    }

    [Theory]
    [MemberData(nameof(BothModesAndMissingEvidence))]
    public async Task A_strict_revocation_without_a_fresh_attestation_is_refused_as_unavailable(bool rcsi, string evidence)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var recovery = await w.HealthyRecoveryAsync();
        await w.EntraIndependentAdminAsync("other");
        await MakeEvidenceUnavailableAsync(w, evidence);

        Exception? refusal;
        await using (var db = w.Scratch.NewDb())
            refusal = await Record.ExceptionAsync(() => w.Emergency(db).RevokeAsync(confirmFallback: true, Cli).AsTask());

        refusal.Should().BeOfType<GovernanceConflictException>().Which.Code.Should().Be(GovernanceConflictCodes.AdministratorViabilityUnavailable);
        (await w.DesignatedAsync(recovery)).Should().BeTrue();
        (await w.RowsAsync(AuditActionNames.AdministratorViabilityBlocked, recovery)).Should().ContainSingle()
            .Which.Fields[ConfigurationEvidenceNames.AuditDetailName].Should().Be(ConfigurationEvidenceNames.Unavailable);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_revocation_when_nobody_is_designated_is_refused_before_the_invariants_and_records_nothing(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var admin = await w.EntraIndependentAdminAsync("admin");
        await w.AttestAsync(ServiceViabilityWorld.AtStart());

        await using (var db = w.Scratch.NewDb())
            (await Record.ExceptionAsync(() => w.Emergency(db).RevokeAsync(confirmFallback: true, Cli).AsTask()))
                .Should().BeOfType<EmergencyAccessException>().Which.Code.Should().Be(EmergencyAccessErrorCodes.NotDesignated);

        (await w.AnyRowsAsync(admin)).Should().Be(0);
    }

    // ================================================================================================ the gate

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_and_a_revocation_take_the_gate_and_wait_for_another_change_to_administrator_access(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var target = await w.EntraIndependentAdminAsync("target");
        await w.EntraIndependentAdminAsync("other");
        await w.AttestAsync(ServiceViabilityWorld.AtStart());

        await using (var holder = await HoldTheGateAsync(w.Scratch))
        {
            var designate = Task.Run(async () =>
            {
                await using var db = w.Scratch.NewDb();
                await w.Emergency(db).DesignateAsync(new UserId(target), Cli);
            });

            (await WaitsOnTheGateAsync(w.Scratch, designate)).Should().BeTrue("a designation is a change to administrator access and queues on the gate");
            (await w.DesignatedAsync(target)).Should().BeFalse();
            await holder.RollbackAsync();
            await designate.WaitAsync(Patience);
        }

        (await w.DesignatedAsync(target)).Should().BeTrue();

        await using (var holder = await HoldTheGateAsync(w.Scratch))
        {
            var revoke = Task.Run(async () =>
            {
                await using var db = w.Scratch.NewDb();
                await w.Emergency(db).RevokeAsync(confirmFallback: true, Cli);
            });

            (await WaitsOnTheGateAsync(w.Scratch, revoke)).Should().BeTrue("a revocation queues on the gate too");
            await holder.RollbackAsync();
            await revoke.WaitAsync(Patience);
        }

        (await w.DesignatedAsync(target)).Should().BeFalse();
    }

    // ================================================================================================ concurrent reducers

    [Theory]
    [MemberData(nameof(BothModesAndOrders))]
    public async Task A_designation_and_the_disablement_of_the_other_viable_administrator_never_both_commit(bool rcsi, bool designationFirst)
    {
        // Two administrators who sign in only with their Local credentials. Designating one and disabling the other would each be safe
        // alone, and together leave no ordinary administrator able to sign in. Both take the gate, so the second is decided on what the
        // first committed and is refused.
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var a = await w.EntraIndependentAdminAsync("a");
        var b = await w.EntraIndependentAdminAsync("b");
        await w.AttestAsync(ServiceViabilityWorld.Established(), registration: true);

        Task Designate(PauseAt? pause) => Task.Run(async () =>
        {
            await using var db = pause is null ? w.Scratch.NewDb() : w.Scratch.NewDb(pause);
            await w.Emergency(db).DesignateAsync(new UserId(a), Cli);
        });

        Task Disable(PauseAt? pause) => Task.Run(async () =>
        {
            await using var db = pause is null ? w.Scratch.NewDb() : w.Scratch.NewDb(pause);
            await w.World.Service(db, a, advisoryAllowsAll: true, coordinator: ServiceViabilityWorld.Coordinator(db))
                .SetLocalCredentialDisabledAsync(new UserId(b), disabled: true);
        });

        // The first runs everything but its rows and its commit, holding the gate.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = designationFirst ? Designate(pause) : Disable(pause);
        await ReachedAsync(pause, first);

        var second = designationFirst ? Disable(null) : Designate(null);
        (await WaitsOnTheGateAsync(w.Scratch, second)).Should().BeTrue("the second change to administrator access waits for the first");

        pause.Release();
        await first.WaitAsync(Patience);
        var refused = await Record.ExceptionAsync(() => second.WaitAsync(Patience));

        refused.Should().BeOfType<GovernanceConflictException>("the second change was decided on what the first committed");
        (await w.DesignatedAsync(a)).Should().Be(designationFirst);
        (await w.CredentialDisabledAsync(b)).Should().Be(!designationFirst);
        var viable = await w.Scratch.ScalarAsync<int>($"""
            SELECT COUNT(*) FROM Users u JOIN UserRoles r ON r.UserId = u.Id AND r.RoleId = N'{ServiceViabilityWorld.Admin}'
              JOIN LocalCredentials c ON c.UserId = u.Id AND c.IsDisabled = 0
             WHERE u.IsBreakGlassEligible = 0;
            """);
        viable.Should().Be(1, "an ordinary administrator who can sign in remains");
    }

    // ================================================================================================ provisioning

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Provisioning_waits_for_the_gate_and_refuses_when_a_designation_committed_while_it_waited(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var existing = await w.EntraIndependentAdminAsync("existing");
        var usersBefore = await w.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;");

        await using (var holder = await HoldTheGateAsync(w.Scratch))
        {
            var provision = Task.Run(async () =>
            {
                await using var db = w.Scratch.NewDb();
                return await w.Emergency(db).ProvisionAsync(NewPassword, Cli);
            });

            (await WaitsOnTheGateAsync(w.Scratch, provision)).Should().BeTrue("provisioning is a change to administrator access");

            // The change that held the gate designates an account and commits.
            await holder.ExecuteAsync($"UPDATE Users SET IsBreakGlassEligible = 1 WHERE Id = N'{existing}';");
            await holder.CommitAsync();

            (await Record.ExceptionAsync(() => provision.WaitAsync(Patience)))
                .Should().BeOfType<EmergencyAccessException>().Which.Code.Should().Be(EmergencyAccessErrorCodes.AlreadyDesignated);
        }

        (await w.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(usersBefore, "no account was created beside the designation");
        (await w.DesignatedCountAsync()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Provisioning_adds_a_designated_administrator_under_the_gate_without_reading_any_configuration_evidence(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);

        // No attestation at all: provisioning is a restorative that can only add, so it is always available.
        UserId id;
        await using (var db = w.Scratch.NewDb())
            id = await w.Emergency(db).ProvisionAsync(NewPassword, Cli);

        (await w.DesignatedAsync(id.Value)).Should().BeTrue();
        (await w.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM LocalCredentials WHERE UserId = N'{id.Value}' AND IsDisabled = 0;")).Should().Be(1);
        (await w.RowsAsync(AuditActionNames.RecoveryAccessProvisioned, id.Value)).Should().ContainSingle()
            .Which.Fields.Should().NotContainKey(ConfigurationEvidenceNames.AuditDetailName, "a restorative records no evidence class");
    }

    // ================================================================================================ L-P3: an administrator's password change

    private Task SetPasswordAsync(ServiceViabilityWorld w, string actor, string target, PauseAt? pause) => Task.Run(async () =>
    {
        await using var db = pause is null ? w.Scratch.NewDb() : w.Scratch.NewDb(pause);
        await w.World.Service(db, actor, advisoryAllowsAll: true, coordinator: ServiceViabilityWorld.Coordinator(db))
            .SetLocalPasswordAsync(new UserId(target), NewPassword);
    });

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_waits_for_an_administrators_password_change_that_holds_the_gate_and_both_apply_in_order(bool rcsi)
    {
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var target = await w.EntraIndependentAdminAsync("target");
        var actor = await w.EntraIndependentAdminAsync("actor");
        await w.AttestAsync(ServiceViabilityWorld.AtStart());
        var hashBefore = await w.PasswordHashAsync(target);

        // Parked just after its locked read of the designation: it holds the gate and the user's row.
        var pause = new PauseAt(c => c.CommandText.Contains("FROM [Users] WITH (", StringComparison.Ordinal), after: true);
        var setPassword = SetPasswordAsync(w, actor, target, pause);
        await ReachedAsync(pause, setPassword);

        var designate = Task.Run(async () =>
        {
            await using var db = w.Scratch.NewDb();
            await w.Emergency(db).DesignateAsync(new UserId(target), Cli);
        });
        (await WaitsOnTheGateAsync(w.Scratch, designate)).Should().BeTrue("the designation waits for the password change that holds the gate");

        pause.Release();
        await setPassword.WaitAsync(Patience);
        await designate.WaitAsync(Patience);

        (await w.PasswordHashAsync(target)).Should().NotBe(hashBefore, "the password change applied first, on an ordinary account");
        (await w.DesignatedAsync(target)).Should().BeTrue("then the designation applied");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_password_change_to_an_account_designated_after_the_gate_was_taken_is_refused_by_the_locked_recheck(bool rcsi)
    {
        // A writer of the designation that did not take the gate (here a statement of its own) commits between the password change's read
        // of the user and its write. The locked read of the stored designation, immediately before the write, refuses it.
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var target = await w.EntraIndependentAdminAsync("target");
        var actor = await w.EntraIndependentAdminAsync("actor");
        var hashBefore = await w.PasswordHashAsync(target);

        var pause = PauseAt.BeforeStatement("FROM [Users] WITH (");
        var setPassword = SetPasswordAsync(w, actor, target, pause);
        await ReachedAsync(pause, setPassword);
        await w.Scratch.ExecuteAsync($"UPDATE Users SET IsBreakGlassEligible = 1 WHERE Id = N'{target}';");

        pause.Release();
        var refusal = await Record.ExceptionAsync(() => setPassword.WaitAsync(Patience));

        refusal.Should().BeOfType<EmergencyAccessException>().Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        (await w.PasswordHashAsync(target)).Should().Be(hashBefore, "the system-managed account's password was not changed");
        (await w.RowsAsync(AuditActionNames.PasswordSet, target)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Control_the_same_interleaving_through_a_writer_without_the_gate_or_the_recheck_reaches_the_designated_account(bool rcsi)
    {
        // The defect the two cases above exclude, shown on the same interleaving: a write decided on a copy read before the designation
        // changes the password of an account that is designated by the time it writes. This is what the gate and the recheck prevent.
        var w = await ServiceViabilityWorld.FreshAsync(_catalogs, rcsi);
        var target = await w.EntraIndependentAdminAsync("target");
        var hashBefore = await w.PasswordHashAsync(target);

        await using var db = w.Scratch.NewDb();
        var copy = (await new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance).GetByIdAsync(new UserId(target)))!;
        EmergencyAccessIdentity.IsSystemManaged(copy).Should().BeFalse("the copy says the account is ordinary");
        await w.Scratch.ExecuteAsync($"UPDATE Users SET IsBreakGlassEligible = 1 WHERE Id = N'{target}';");
        var (hash, salt) = new LocalPasswordHasher().HashPassword(NewPassword);
        await new SqlLocalCredentialRepository(db).SetPasswordAsync(copy.Id, hash, salt, new LocalPasswordHasher().AlgorithmName);

        (await w.DesignatedAsync(target)).Should().BeTrue();
        (await w.PasswordHashAsync(target)).Should().NotBe(hashBefore, "without the gate and the recheck, the designated account's password changes");
    }
}
