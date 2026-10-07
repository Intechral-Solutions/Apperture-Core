using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.2 identity and governance writers against SQL Server (audit hardening design gate §13, §22; M6, M7): user
/// administration, Recovery Access administration, native MFA, the first administrator and the recovery restore. Each change commits with its recorder row or not at all;
/// an uncertain commit is verified before anything reruns; a refusal is decided inside the transaction and recorded after
/// it rolled back; a request that changes nothing records nothing. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class IdentityGovernanceAuditSqlServerTests : IAsyncLifetime
{
    private static readonly PermissionDescriptor UserRead = new(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance));
    private static readonly PermissionDescriptor UserManage = new(ActionConstants.UserManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance));
    private static readonly string PlatformAdmin = BootstrapConstants.PlatformAdminRoleId;

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public IdentityGovernanceAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud12Governance_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- Roles and permission diffs ----

    [Fact]
    public async Task Role_creation_and_permission_replacement_commit_with_rows_holding_the_committed_diff()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);

        Role role;
        await using (var db = _db.NewDb())
            role = await Service(db, principal).CreateRoleAsync("Operators", null, [UserRead]);
        await using (var db = _db.NewDb())
            await Service(db, principal).SetRolePermissionsAsync(role.Id, [UserManage]);

        var rows = await RowsAsync("role", role.Id.Value);
        rows.Select(r => r["Action"]).Should().Equal(AuditActionNames.RoleCreated, AuditActionNames.PermissionAssignmentUpdated);
        rows.Should().OnlyContain(r => (string)r["Producer"]! == "recorder" && (string)r["Actor"]! == admin && (string)r["Basis"]! == "change");

        using var created = JsonDocument.Parse((string)rows[0]["Details"]!);
        created.RootElement.GetProperty("permissionsAdded").EnumerateArray().Select(e => e.GetString()).Should().Equal("user.read@User:instance");

        using var replaced = JsonDocument.Parse((string)rows[1]["Details"]!);
        replaced.RootElement.GetProperty("permissionsAdded").EnumerateArray().Select(e => e.GetString()).Should().Equal("user.manage@User:instance");
        replaced.RootElement.GetProperty("permissionsRemoved").EnumerateArray().Select(e => e.GetString()).Should().Equal("user.read@User:instance");
        (await _db.ScalarAsync<string>($"SELECT PermissionsJson FROM Roles WHERE Id = N'{role.Id.Value}';"))
            .Should().Contain("user.manage").And.NotContain("user.read");
    }

    [Fact]
    public async Task When_the_audit_insert_fails_a_permission_change_rolls_back_with_it()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        Role role;
        await using (var db = _db.NewDb())
            role = await Service(db, principal).CreateRoleAsync("Readers", null, [UserRead]);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Service(db, principal).SetRolePermissionsAsync(role.Id, [UserManage]).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        (await _db.ScalarAsync<string>($"SELECT PermissionsJson FROM Roles WHERE Id = N'{role.Id.Value}';"))
            .Should().Contain("user.read").And.NotContain("user.manage", "the change never commits without its row");
        (await RowsAsync("role", role.Id.Value)).Select(r => r["Action"]).Should().Equal(AuditActionNames.RoleCreated);
    }

    [Fact]
    public async Task A_role_delete_whose_acknowledgement_is_lost_is_reported_as_done_not_as_not_found()
    {
        var (admin, _) = await AddUserAsync("admin");
        var principal = new Principal(admin);
        Role role;
        await using (var db = _db.NewDb())
            role = await Service(db, principal).CreateRoleAsync("Temporary");
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using (var db = _db.NewDb(lost, attempts))
            await Service(db, principal).DeleteRoleAsync(role.Id);

        attempts.Started.Should().Be(1, "verification confirmed the commit, so nothing reran");
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM Roles WHERE Id = N'{role.Id.Value}';")).Should().Be(0);
        (await RowsAsync("role", role.Id.Value)).Select(r => r["Action"]).Should().Equal(AuditActionNames.RoleCreated, AuditActionNames.RoleDeleted);
    }

    // ---- Assignments: no-ops and refusals ----

    [Fact]
    public async Task Assigning_a_held_role_records_nothing_and_removing_the_last_administrator_is_refused_after_rollback()
    {
        var (admin, _) = await AddUserAsync("admin");
        await _db.ExecuteAsync($"INSERT INTO UserRoles (UserId, RoleId) VALUES (N'{admin}', N'{PlatformAdmin}');");
        var principal = new Principal(admin);

        await using (var db = _db.NewDb())
            await Service(db, principal).AssignRoleAsync(new UserId(admin), new RoleId(PlatformAdmin));
        (await RowsAsync("user", admin)).Should().BeEmpty("an assignment that already exists changes nothing");

        await using (var db = _db.NewDb())
        {
            var act = () => Service(db, principal).RemoveRoleAsync(new UserId(admin), new RoleId(PlatformAdmin)).AsTask();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*last platform administrator*");
        }

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{admin}' AND RoleId = N'{PlatformAdmin}';")).Should().Be(1);
        var refusal = (await RowsAsync("user", admin)).Should().ContainSingle().Which;
        refusal["Action"].Should().Be(AuditActionNames.BlockedLastPlatformAdminRemoval);
        refusal["Producer"].Should().Be("recorder");
        refusal["Basis"].Should().Be("attempt");
        refusal["OutcomeCode"].Should().Be("rejected");
        refusal["Actor"].Should().Be(admin);
    }

    // ---- Identity links: the persisted id is the target ----

    [Fact]
    public async Task Removing_an_identity_link_targets_its_persisted_id()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (user, _) = await AddUserAsync("linked");
        const string tenant = "11111111-1111-1111-1111-111111111111";
        const string objectId = "22222222-2222-2222-2222-222222222222";
        var linkId = IdentityLink.NewLinkId();
        await _db.ExecuteAsync($"""
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt)
            VALUES (N'{linkId}', N'{user}', 0, N'{objectId}', N'{tenant}', SYSDATETIMEOFFSET());
            """);

        await using (var db = _db.NewDb())
            await Service(db, new Principal(admin)).RemoveIdentityLinkAsync(new UserId(user), IdentitySource.Entra, objectId);

        var row = (await RowsAsync("identity_link", linkId)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.IdentityLinkRemoved);
        using var json = JsonDocument.Parse((string)row["Details"]!);
        json.RootElement.GetProperty("userId").GetString().Should().Be(user);
        json.RootElement.GetProperty("tenantId").GetString().Should().Be(tenant);
        json.RootElement.GetProperty("objectId").GetString().Should().Be(objectId);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM IdentityLinks WHERE Id = N'{linkId}';")).Should().Be(0);

        // Removing it again finds nothing to remove: it succeeds and records nothing.
        await using (var db = _db.NewDb())
            await Service(db, new Principal(admin)).RemoveIdentityLinkAsync(new UserId(user), IdentitySource.Entra, objectId);
        (await RowsAsync("identity_link", linkId)).Should().ContainSingle();
    }

    // ---- Recovery Access: the corrected actor, and no marker row ----

    [Fact]
    public async Task A_change_refused_to_protect_Recovery_Access_records_who_attempted_it_not_the_protected_account()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (protectedUser, _) = await AddUserAsync("recovery");
        await _db.ExecuteAsync($"""
            UPDATE Users SET IsBreakGlassEligible = 1 WHERE Id = N'{protectedUser}';
            INSERT INTO UserRoles (UserId, RoleId) VALUES (N'{admin}', N'{PlatformAdmin}'), (N'{protectedUser}', N'{PlatformAdmin}');
            """);
        var principal = new Principal(admin);

        await using (var db = _db.NewDb())
        {
            var emergency = Emergency(db);
            var act = () => Service(db, principal, emergency).RemoveRoleAsync(new UserId(protectedUser), new RoleId(PlatformAdmin)).AsTask();
            (await act.Should().ThrowAsync<EmergencyAccessException>()).Which.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
        }

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{protectedUser}';")).Should().Be(1);
        var row = (await RowsAsync("user", protectedUser)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.UnsafeOperationBlocked);
        row["Actor"].Should().Be(admin, "the person who attempted the change, not the protected account (§15.3)");
        row["ActorKind"].Should().Be("user");
        row["AttributionIssue"].Should().BeNull("recorder rows carry no defect");
    }

    [Fact]
    public async Task A_Recovery_Access_session_marks_its_rows_and_writes_no_marker_row()
    {
        var (recovery, _) = await AddUserAsync("recovery");
        var (target, _) = await AddUserAsync("target");
        var principal = new RecoveryAccessPrincipal(recovery);

        await using (var db = _db.NewDb())
            await Service(db, principal).AssignRoleAsync(new UserId(target), new RoleId(PlatformAdmin));

        var row = (await RowsAsync("user", target)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.RoleAssigned);
        using var json = JsonDocument.Parse((string)row["Details"]!);
        json.RootElement.GetProperty(AuditDetailSchema.ViaRecoveryAccessProperty).GetBoolean().Should().BeTrue();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = N'governance.break_glass.admin_operation';"))
            .Should().Be(0, "the pre-mutation marker row is gone");
    }

    [Fact]
    public async Task An_ordinary_session_never_marks_its_rows()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (target, _) = await AddUserAsync("target");

        await using (var db = _db.NewDb())
            await Service(db, new Principal(admin)).AssignRoleAsync(new UserId(target), new RoleId(PlatformAdmin));

        ((string)(await RowsAsync("user", target)).Single()["Details"]!).Should().NotContain(AuditDetailSchema.ViaRecoveryAccessProperty);
    }

    // ---- Recovery Access administration (command line) ----

    [Fact]
    public async Task Provisioning_Recovery_Access_commits_the_account_its_credential_and_its_row_together()
    {
        UserId id;
        await using (var db = _db.NewDb())
            id = await Emergency(db).ProvisionAsync("quiet-maple-lantern-harbor", actorId: null);

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM LocalCredentials WHERE UserId = N'{id.Value}';")).Should().Be(1);
        var row = (await RowsAsync("user", id.Value)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.RecoveryAccessProvisioned);
        row["Actor"].Should().Be(ActorLiterals.EmergencyCli);
        row["ActorKind"].Should().Be("system");
        ((string)row["Details"]!).Should().NotContain("quiet-maple-lantern-harbor");
    }

    [Fact]
    public async Task When_the_audit_insert_fails_no_Recovery_Access_account_is_left_without_its_row()
    {
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Emergency(db).ProvisionAsync("quiet-maple-lantern-harbor", actorId: null).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE IsBreakGlassEligible = 1;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM LocalCredentials;")).Should().Be(0);
    }

    // ---- Native MFA ----

    [Fact]
    public async Task Recovery_codes_regenerated_after_a_lost_acknowledgement_are_the_codes_that_were_stored()
    {
        var (user, _) = await AddUserAsync("mfa");
        var userId = new UserId(user);
        await using (var db = _db.NewDb())
        {
            var totp = new SqlNativeTotpCredentialRepository(db);
            await totp.UpsertPendingAsync(userId, [1, 2, 3], "test-key", DateTimeOffset.UtcNow);
            await totp.MarkEnrolledAsync(userId, 1, DateTimeOffset.UtcNow);
        }

        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        IReadOnlyList<string> codes;
        await using (var db = _db.NewDb(lost, attempts))
            codes = await Mfa(db).RegenerateRecoveryCodesAsync(userId, AuditActor.EmergencyCli);

        attempts.Started.Should().Be(1);
        codes.Should().HaveCount(10);
        var generated = (await RowsAsync("user", user)).Should().ContainSingle().Which;
        generated["Action"].Should().Be(AuditActionNames.RecoveryCodesGenerated);
        generated["Actor"].Should().Be(ActorLiterals.EmergencyCli);
        ((string)generated["Details"]!).Should().NotContainAny(codes.ToArray());

        // The codes returned after verification are the ones stored: each one consumes, and the sign-in is the actor.
        await using (var db = _db.NewDb())
            (await Mfa(db).ConsumeRecoveryCodeAsync(userId, codes[0])).Should().BeTrue();
        await using (var db = _db.NewDb())
            (await Mfa(db).ConsumeRecoveryCodeAsync(userId, codes[0])).Should().BeFalse("a used code changes nothing");

        var consumed = (await RowsAsync("user", user)).Where(r => (string)r["Action"]! == AuditActionNames.RecoveryCodeConsumed).Should().ContainSingle().Which;
        consumed["Actor"].Should().Be(user, "the user being authenticated");
        consumed["ActorKind"].Should().Be("user");
    }

    // ---- First-administrator bootstrap ----

    [Fact]
    public async Task The_first_administrator_commits_with_its_anonymous_row_or_not_at_all()
    {
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Bootstrap(db).TryCreateFirstAdminAsync("First Admin", IdentitySource.Local, "firstadmin", "quiet-maple-lantern-harbor");
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Users;")).Should().Be(0, "no administrator without its row");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM LocalCredentials;")).Should().Be(0);

        FirstAdminBootstrapResult result;
        await using (var db = _db.NewDb())
            result = await Bootstrap(db).TryCreateFirstAdminAsync("First Admin", IdentitySource.Local, "firstadmin", "quiet-maple-lantern-harbor");

        result.Kind.Should().Be(FirstAdminBootstrapResultKind.Success);
        var row = (await RowsAsync("user", result.UserId!.Value)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.FirstAdminCreated);
        row["Actor"].Should().Be("anonymous");
        ((string)row["Details"]!).Should().NotContain("quiet-maple-lantern-harbor");
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{result.UserId!.Value}' AND RoleId = N'{PlatformAdmin}';")).Should().Be(1);
    }

    // ---- Configured administrators (BootstrapPlatformAdmin at host start; AUD-1.2 review) ----

    [Fact]
    public async Task A_configured_administrator_commits_with_its_rows_or_not_at_all_and_an_assignment_is_recorded_once()
    {
        var options = new BootstrapPlatformAdminOptions
        {
            Enabled = true,
            CreateUserIfMissing = true,
            Principals = { new BootstrapPlatformAdminPrincipalOptions { IdentitySource = "Local", ExternalId = "configured.admin" } },
        };
        Task ApplyAsync(AppertureDbContext db) => BootstrapPlatformAdminAssignment.ExecuteAsync(
            options, new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance), new SqlRoleRepository(db), null, NewUnit(db, null));

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => ApplyAsync(db);
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Username = N'configured.admin';")).Should().Be(0, "no administrator without its rows");

        await using (var db = _db.NewDb())
            await ApplyAsync(db);
        var userId = await _db.ScalarAsync<string>("SELECT Id FROM Users WHERE Username = N'configured.admin';");
        var rows = await RowsAsync("user", userId);
        rows.Select(r => r["Action"]).Should().Equal(AuditActionNames.ConfiguredAdminCreated, AuditActionNames.RoleAssigned);
        rows.Should().OnlyContain(r => (string)r["Actor"]! == ActorLiterals.SystemBootstrapConfiguration && (string)r["ActorKind"]! == "system");
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{userId}' AND RoleId = N'{PlatformAdmin}';")).Should().Be(1);

        // An administrator removed by hand gets the role back at the next start, with one assignment row; a start that finds
        // the role held changes nothing and writes no row.
        await _db.ExecuteAsync($"DELETE FROM UserRoles WHERE UserId = N'{userId}' AND RoleId = N'{PlatformAdmin}';");
        await using (var db = _db.NewDb(new LostCommitAcknowledgement()))
            await ApplyAsync(db);
        await using (var db = _db.NewDb())
            await ApplyAsync(db);

        (await RowsAsync("user", userId)).Select(r => r["Action"]).Should().Equal(
            AuditActionNames.ConfiguredAdminCreated, AuditActionNames.RoleAssigned, AuditActionNames.RoleAssigned);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{userId}' AND RoleId = N'{PlatformAdmin}';")).Should().Be(1);
    }

    private static FirstAdminBootstrapService Bootstrap(AppertureDbContext db) =>
        new(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlRoleRepository(db),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            NewUnit(db, null));

    // ---- M6 per service (gate §22 AUD-1.2): the remaining audit-failure and lost-acknowledgement paths ----

    [Fact]
    public async Task A_rename_whose_audit_fails_keeps_the_old_name_and_its_retry_is_audited_once()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (target, targetName) = await AddUserAsync("dana");
        var principal = new Principal(admin);
        var requested = NewUsername("dana.new");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Service(db, principal).RenameUsernameAsync(new UserId(target), requested).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }
        (await _db.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{target}';")).Should().Be(targetName);

        await using (var db = _db.NewDb())
            await Service(db, principal).RenameUsernameAsync(new UserId(target), requested);

        (await _db.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{target}';")).Should().Be(requested);
        var row = (await RowsAsync("user", target)).Should().ContainSingle("the retry is a new operation, audited once").Which;
        row["Action"].Should().Be(AuditActionNames.UsernameChanged);
    }

    [Fact]
    public async Task Revoking_Recovery_Access_after_a_lost_acknowledgement_is_reported_done_with_one_row()
    {
        UserId recovery;
        await using (var db = _db.NewDb())
            recovery = await Emergency(db).ProvisionAsync("quiet-maple-lantern-harbor", actorId: null);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using (var db = _db.NewDb(lost, attempts))
            await Emergency(db).RevokeAsync(confirmFallback: true, actorId: null);

        attempts.Started.Should().Be(1, "verification confirmed the commit, so nothing reran");
        (await _db.ScalarAsync<bool>($"SELECT IsBreakGlassEligible FROM Users WHERE Id = N'{recovery.Value}';")).Should().BeFalse();
        (await RowsAsync("user", recovery.Value)).Count(r => (string)r["Action"]! == AuditActionNames.RecoveryAccessRevoked).Should().Be(1);
    }

    [Fact]
    public async Task When_the_audit_insert_fails_regenerated_recovery_codes_are_not_stored()
    {
        var (user, _) = await AddUserAsync("mfa");
        var userId = new UserId(user);
        await using (var db = _db.NewDb())
        {
            var totp = new SqlNativeTotpCredentialRepository(db);
            await totp.UpsertPendingAsync(userId, [1, 2, 3], "test-key", DateTimeOffset.UtcNow);
            await totp.MarkEnrolledAsync(userId, 1, DateTimeOffset.UtcNow);
        }

        var before = await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId = N'{user}';");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Mfa(db).RegenerateRecoveryCodesAsync(userId, AuditActor.EmergencyCli).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId = N'{user}';")).Should().Be(before);
        (await RowsAsync("user", user)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_first_administrator_whose_acknowledgement_is_lost_is_reported_created_once()
    {
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        FirstAdminBootstrapResult result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await Bootstrap(db).TryCreateFirstAdminAsync("First Admin", IdentitySource.Local, "firstadmin", null);

        attempts.Started.Should().Be(1);
        result.Kind.Should().Be(FirstAdminBootstrapResultKind.Success, "a verified commit is success, never 'already initialized'");
        (await RowsAsync("user", result.UserId!.Value)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_recovery_restore_whose_audit_fails_assigns_nothing_and_one_whose_acknowledgement_is_lost_is_reported_restored_once()
    {
        var (user, _) = await AddUserAsync("restored");
        var request = new Apperture.Bootstrap.Models.BootstrapRecoveryRequest
        {
            ActionType = Apperture.Bootstrap.Models.BootstrapRecoveryAction.RestorePlatformAccess,
            TargetUserId = user,
            Reason = "operator recovery",
        };
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Recovery(db).ExecuteRecoveryAsync(request);
            await act.Should().ThrowAsync<Exception>();
        }
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{user}' AND RoleId = N'{PlatformAdmin}';")).Should().Be(0);

        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        Apperture.Bootstrap.Models.BootstrapRecoveryResult result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await Recovery(db).ExecuteRecoveryAsync(request);

        attempts.Started.Should().Be(1);
        result.Success.Should().BeTrue("a verified commit is the restore, never 'target not found' or 'already restored'");
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{user}' AND RoleId = N'{PlatformAdmin}';")).Should().Be(1);
        var row = (await RowsAsync("user", user)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.RestorePlatformAccessExecuted);
        row["Actor"].Should().Be("recovery");
        row["ActorKind"].Should().Be("system");
    }

    // ---- helpers ----

    private async Task<(string Id, string Username)> AddUserAsync(string stem)
    {
        var id = NewUserId();
        var username = NewUsername(stem);
        await _db.AddUserAsync(id, username);
        return (id, username);
    }

    private UserManagementService Service(AppertureDbContext db, IPrincipalContext principal, IEmergencyAccessService? emergency = null)
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        registry.RegisterPermission(UserRead, ResourceFamily.Core);
        registry.RegisterPermission(UserManage, ResourceFamily.Core);
        return RenameService(db, principal, NewUnit(db, principal), Events(principal), emergency, registry);
    }

    private SqlAuditEventWriter Events(IPrincipalContext principal) =>
        new(_db.Options(), principal, HarnessCatalog, null, null, null);

    private static EmergencyAccessService Emergency(AppertureDbContext db) =>
        new(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlRoleRepository(db),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            new NoMfa(),
            TimeProvider.System,
            NullLogger<EmergencyAccessService>.Instance,
            NewUnit(db, null));

    private static NativeMfaService Mfa(AppertureDbContext db) =>
        new(
            new SqlNativeTotpCredentialRepository(db),
            new SqlNativeMfaRecoveryCodeRepository(db),
            new LocalPasswordHasher(),
            new IdentityProtector(),
            TimeProvider.System,
            NullLogger<NativeMfaService>.Instance,
            NewUnit(db, null));

    /// <summary>The recovery service resolves its repositories and unit from a scope, as the host does.</summary>
    private RealBootstrapRecoveryService Recovery(AppertureDbContext db)
    {
        var services = new ServiceCollection()
            .AddSingleton<IUserRepository>(new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance))
            .AddSingleton<IRoleRepository>(new SqlRoleRepository(db))
            .AddSingleton<IAuditedUnitOfWork>(NewUnit(db, null))
            .AddSingleton<IAuditEventWriter>(new SqlAuditEventWriter(_db.Options(), null, HarnessCatalog, null, null, null))
            .BuildServiceProvider();
        return new RealBootstrapRecoveryService(new AuthorizedRecovery(), new NoBootstrapAudit(), services.GetRequiredService<IServiceScopeFactory>());
    }

    private Task<List<Dictionary<string, object?>>> RowsAsync(string targetType, string targetId) =>
        _db.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetType = N'{targetType}' AND TargetId = N'{targetId}' ORDER BY Id;");

    /// <summary>A session established through Recovery Access.</summary>
    private sealed class RecoveryAccessPrincipal(string userId) : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId(userId), "Recovery Access", IdentitySource.Local, isBreakGlassSession: true));
    }

    private sealed class IdentityProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
    }

    /// <summary>Recovery Access posture is not exercised here.</summary>
    private sealed class NoMfa : INativeMfaService
    {
        public ValueTask<bool> IsEnrolledAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<bool> IsRequiredAsync(User user, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<NativeMfaEnrollmentChallenge> BeginEnrollmentAsync(UserId userId, string accountName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<string>> CompleteEnrollmentAsync(UserId userId, string totpCode, AuditActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> VerifyTotpAsync(UserId userId, string totpCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> ConsumeRecoveryCodeAsync(UserId userId, string recoveryCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(UserId userId, AuditActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ResetAsync(UserId userId, AuditActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> CountUnusedRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
    }

    private sealed class AuthorizedRecovery : Apperture.Bootstrap.IBootstrapRecoveryAuthorizer
    {
        public Task<Apperture.Bootstrap.Models.BootstrapRecoveryAuthorizationResult> AuthorizeAsync(Apperture.Bootstrap.Models.BootstrapRecoveryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Apperture.Bootstrap.Models.BootstrapRecoveryAuthorizationResult { Authorized = true });
    }

    private sealed class NoBootstrapAudit : Apperture.Bootstrap.IBootstrapAuditWriter
    {
        public Task WriteAsync(Apperture.Bootstrap.Models.BootstrapChangeAuditRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteRecoveryAsync(Apperture.Bootstrap.Models.BootstrapRecoveryAuditRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
