using Apperture.Core.Application.Audit;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Wiring guards for the authorization data step: it runs on every start through the supported migration entry point, after the
/// last migration, and its summary is a declared catalog action written with the change it describes. The behaviour is proven
/// against SQL Server in <c>RoleAuthorizationDataStepSqlServerTests</c>.
/// </summary>
public sealed class RoleAuthorizationDataStepWiringTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void The_migrator_runs_the_step_after_the_last_migration_and_does_not_swallow_a_failure()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Apperture.Persistence", "Apperture.Persistence.SqlServer", "AppertureDatabaseMigrator.cs"));

        var lastMigration = source.LastIndexOf("db.Database.MigrateAsync(", StringComparison.Ordinal);
        var step = source.IndexOf("RoleAuthorizationDataStep.RunAsync(", StringComparison.Ordinal);

        lastMigration.Should().BeGreaterThan(-1);
        step.Should().BeGreaterThan(lastMigration, "the step reads and writes Roles and AuditLogs, which exist only once every migration has run");
        var statement = source[step..source.IndexOf(';', step)];
        statement.Should().StartWith("RoleAuthorizationDataStep.RunAsync(db, logger, cancellationToken)");
        source[(step - 80)..step].Should().NotContain("catch", "a failure must reach the host start, which then fails");
        source[step..].Should().NotContain("catch", "nothing after the step swallows its failure");
    }

    [Fact]
    public void The_summary_is_a_required_mutation_declared_in_the_catalog_with_no_target()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.AuthorizationGrantsMigrated);

        definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation, "the summary commits with the changes it describes");
        definition.Category.Should().Be(AuditCategories.Governance);
        definition.Severity.Should().Be(AuditSeverities.Elevated);
        definition.Basis.Should().Be(AuditBases.Change);
        definition.TargetType.Should().BeNull("one summary describes many roles");
        definition.Details.Should().NotBeNull();
        definition.Details!.Fields.Select(f => f.Name).Should().BeEquivalentTo(
            ["rolesChanged", "grantsAdded", "grantsRemoved", "unreadableRoleCount", "broadGrantRoleCount", "changedRoles", "changes", "unreadableRoles", "broadGrantRoles"]);
        definition.IntroducedIn.Should().Be("IAM-10.2a");
    }

    [Fact]
    public void The_summary_records_only_ids_counts_and_permission_identities()
    {
        var details = AuditActionCatalog.Default.Get(AuditActionNames.AuthorizationGrantsMigrated).Details!;

        details.Fields.Should().OnlyContain(f => f.Kind == AuditFieldKind.Count || f.Kind == AuditFieldKind.IdList,
            "no name, no free text, no stored JSON can be recorded by this event");
        details.Fields.Where(f => f.Kind == AuditFieldKind.IdList).Should().OnlyContain(f => f.MaxItems <= 100);
    }

    [Fact]
    public void The_event_producer_bounds_its_lists_and_keeps_the_counts_exact()
    {
        var many = Enumerable.Range(0, 250).Select(i => $"role-{i:000}").ToList();

        var row = AuditEvents.AuthorizationGrantsMigrated(250, 2500, 3, many, many.Select(r => r + "|+x@y:instance").ToList(), [], many);

        ((IReadOnlyList<string>)row.Fields["changedRoles"]!).Should().HaveCount(100);
        ((IReadOnlyList<string>)row.Fields["changes"]!).Should().HaveCount(100);
        row.Fields["rolesChanged"].Should().Be(250);
        row.Fields["grantsAdded"].Should().Be(2500);
        row.Fields["broadGrantRoleCount"].Should().Be(250);
        row.Fields.Should().NotContainKey("unreadableRoles");
    }
}
