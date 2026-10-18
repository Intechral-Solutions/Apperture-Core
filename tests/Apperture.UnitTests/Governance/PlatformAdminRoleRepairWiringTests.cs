using Apperture.Core.Application.Audit;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-10.2a slice 4: the repair's audit action, the startup order that lets it see the whole permission catalog, and the
/// rule that its failure reaches the host start. The behaviour itself is in <see cref="PlatformAdminRoleRepairServiceTests"/>
/// and, against SQL Server, <c>PlatformAdminRoleRepairSqlServerTests</c>.
/// </summary>
public sealed class PlatformAdminRoleRepairWiringTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Governance() => File.ReadAllText(Path.Combine(
        RepositoryRoot(), "src", "Apperture.ControlPlane", "Apperture.ControlPlane.Security", "ServiceCollectionExtensions.Governance.cs"));

    [Fact]
    public void The_summary_is_a_required_mutation_on_the_administrator_role_and_records_only_counts_and_permission_identities()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.AdministratorRoleRepaired);

        definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation, "the summary commits with the change it describes");
        definition.Category.Should().Be(AuditCategories.Governance);
        definition.Severity.Should().Be(AuditSeverities.Elevated);
        definition.Basis.Should().Be(AuditBases.Change);
        definition.TargetType.Should().Be(AuditTargetTypes.Role);
        definition.IntroducedIn.Should().Be("IAM-10.2a");
        definition.Details!.Fields.Select(f => f.Name).Should().BeEquivalentTo(
            ["roleId", "permissionCount", "grantsAdded", "grantsRemoved", "permissionsAdded", "permissionsRemoved"]);
        definition.Details.Fields.Where(f => f.Kind == AuditFieldKind.IdList).Should().OnlyContain(f => f.MaxItems <= 100);
        definition.Details.Fields.Should().OnlyContain(
            f => f.Kind == AuditFieldKind.Count || f.Kind == AuditFieldKind.IdList || f.Name == "roleId",
            "no name, no free text and no stored JSON can be recorded by this event");
    }

    [Fact]
    public void The_repair_starts_after_module_permissions_are_registered_and_before_the_bootstrap_assignment()
    {
        var source = Governance();

        var modules = source.IndexOf("AddHostedService<ModulePermissionRegistrationHostedService>", StringComparison.Ordinal);
        var repair = source.IndexOf("AddHostedService<PlatformAdminRoleRepairService>", StringComparison.Ordinal);
        var bootstrap = source.IndexOf("AddHostedService<BootstrapPlatformAdminAssignmentHostedService>", StringComparison.Ordinal);

        modules.Should().BeGreaterThan(-1);
        repair.Should().BeGreaterThan(modules, "the repair must see every module permission before it computes its target");
        bootstrap.Should().BeGreaterThan(repair, "the first administrator is assigned a role that has already been repaired");
    }

    [Fact]
    public void Migrations_and_the_authorization_data_step_run_before_the_repair_in_the_management_host()
    {
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Apperture.ControlPlane", "Apperture.Management.Host", "Program.cs"));

        var migrations = program.IndexOf("AddApperturePersistenceMigrationsOnStartup", StringComparison.Ordinal);
        var governance = program.IndexOf("AddAppertureGovernance", StringComparison.Ordinal);

        migrations.Should().BeGreaterThan(-1);
        governance.Should().BeGreaterThan(migrations,
            "hosted services start in registration order: the audit table and the Roles table must be current before the audited repair runs");
    }

    [Fact]
    public void The_repair_locks_the_role_before_it_decides_and_writes_it_only_inside_the_audited_unit()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Apperture.ControlPlane", "Apperture.ControlPlane.Security", "PlatformAdminRoleRepairService.cs"));

        var unit = source.IndexOf("unit.ExecuteAsync(", StringComparison.Ordinal);
        var @lock = source.IndexOf(".LockAuthoritySnapshotAsync(", StringComparison.Ordinal);
        var decide = source.IndexOf("PlatformAdminRolePermissionRepair.Plan(", StringComparison.Ordinal);
        var write = source.IndexOf(".UpdatePermissionsAsync(", StringComparison.Ordinal);
        var stage = source.IndexOf(".StageAsync(", StringComparison.Ordinal);

        unit.Should().BeGreaterThan(-1);
        @lock.Should().BeGreaterThan(unit, "the role is read inside the unit's transaction");
        decide.Should().BeGreaterThan(@lock, "the decision is made from the row as locked");
        write.Should().BeGreaterThan(decide);
        stage.Should().BeGreaterThan(write, "the summary row is staged with the change it describes");
        source.Should().NotContain(".GetAsync(", "the only read of the role is the locked one");
        source.Should().NotContain("GetForUserAsync", "it is a system writer and does not run through actor authority");
    }

    [Fact]
    public void The_repair_is_a_named_system_writer_and_does_not_take_the_administrator_set_lock()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Apperture.ControlPlane", "Apperture.ControlPlane.Security", "PlatformAdminRoleRepairService.cs"));

        source.Should().Contain("AuditActor.System(\"administrator-repair\")");
        source.Should().NotContain("sp_getapplock").And.NotContain("platform-administrator-set").And.NotContain("PlatformAdministratorSetGate");
    }

    [Fact]
    public void The_repair_swallows_nothing_so_its_failure_fails_the_host_start()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Apperture.ControlPlane", "Apperture.ControlPlane.Security", "PlatformAdminRoleRepairService.cs"));

        var catches = System.Text.RegularExpressions.Regex.Matches(source, @"\bcatch\b").Count;
        var rethrowing = System.Text.RegularExpressions.Regex.Matches(source, @"\bcatch\b[^{]*\{[^}]*\bthrow;").Count;
        rethrowing.Should().Be(catches, "a repair that cannot complete must stop the host rather than leave the role inconsistent: every catch logs and rethrows");
    }
}
