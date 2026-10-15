using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.EntraRegistrationSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// SEC-2.4 (acceptance A4.2): <c>Sec24_EntraRegistrationDiagnostics</c> moves the eight probe-outcome columns off
/// <c>EntraInstanceRegistrations</c> into <c>EntraRegistrationDiagnostics</c>. Each case starts from an existing installation: a
/// catalog migrated to the last migration before SEC-2.4, seeded in the old schema with varied diagnostics, then upgraded through
/// the supported migrator.
/// <list type="bullet">
/// <item>All eight values of every registration survive unchanged, NULLs and unusual statuses included; the registration rows keep
/// their <c>rowversion</c> and <c>UpdatedUtc</c>.</item>
/// <item>The new table's keys, nullability, lengths and defaults are those the columns had, and the old columns are gone.</item>
/// <item>The repository, the controller's list and detail, and the authentication overview read the same values as before.</item>
/// <item>Creation and deletion keep the diagnostics row's lifecycle; <c>Down</c> restores the columns with their data and the
/// upgrade can be applied again.</item>
/// </list>
/// Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class EntraRegistrationDiagnosticsMigrationSqlServerTests
{
    private const string PreMigration = "20261012100000_Aud14_BindingTargetType";

    private readonly ITestOutputHelper _output;

    public EntraRegistrationDiagnosticsMigrationSqlServerTests(ITestOutputHelper output) => _output = output;

    private static readonly string Detail4000 = string.Concat(Enumerable.Repeat("détail–✓ ", 500))[..4000];
    private static readonly string Summary512 = string.Concat(Enumerable.Repeat("résumé ", 100))[..512];
    private static readonly DateTimeOffset Validated = new(2026, 9, 1, 8, 30, 15, 123, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Searched = new(2026, 9, 2, 22, 5, 0, TimeSpan.Zero);

    /// <summary>One seeded registration: its identity and the eight diagnostic values, or null for a column left to its default.</summary>
    private sealed record Seed(
        string Id,
        bool IsActive,
        int? OpenIdStatus,
        DateTimeOffset? OpenIdUtc,
        string? OpenIdSummary,
        string? OpenIdDetail,
        int? DirectoryStatus,
        DateTimeOffset? DirectoryUtc,
        string? DirectorySummary,
        string? DirectoryDetail);

    private static readonly Seed[] Seeds =
    [
        // Never probed: the columns carry their database defaults.
        new("mig-never-probed", false, null, null, null, null, null, null, null, null),
        // Both probes ran and succeeded; values at the column limits, with non-ASCII text.
        new("mig-succeeded", true, (int)OpenIdConfigurationStatus.Valid, Validated, Summary512, Detail4000,
            (int)DirectoryDiscoveryStatus.Success, Searched, "Directory search succeeded.", null),
        // Both failed in different ways; some fields NULL.
        new("mig-failed", false, (int)OpenIdConfigurationStatus.IssuerMismatch, Validated, "The issuer does not match.", null,
            (int)DirectoryDiscoveryStatus.FailedPermissions, Searched, "Graph refused the request.", "Authorization_RequestDenied"),
        // Statuses at the ends of the enums, and a summary without a time.
        new("mig-edge-statuses", false, (int)OpenIdConfigurationStatus.Unknown, null, "unknown outcome", null,
            (int)DirectoryDiscoveryStatus.Throttled, null, null, "retry-after: 30"),
        // Only the OpenID half has ever run.
        new("mig-openid-only", false, (int)OpenIdConfigurationStatus.InvalidConfiguration, Validated, "Authority is required.", "missing_authority", null, null, null, null),
        // Only the directory half has ever run.
        new("mig-directory-only", false, null, null, null, null, (int)DirectoryDiscoveryStatus.TransientFailure, Searched, "Graph is unavailable.", "HTTP 503"),
    ];

    private async Task<AuditSqlScratch> ExistingInstallationAsync()
    {
        var scratch = await AuditSqlScratch.CreateAtMigrationAsync("Apperture_Sec24Migration_", PreMigration);
        _output.WriteLine(scratch.ExecutionMarker);

        // The old schema really has the columns, and not the new table.
        (await ColumnNamesAsync(scratch, "EntraInstanceRegistrations")).Should().Contain(ProbeColumns);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = N'EntraRegistrationDiagnostics';")).Should().Be(0);

        foreach (var seed in Seeds)
            await InsertOldRegistrationAsync(scratch, seed);
        return scratch;
    }

    private static async Task InsertOldRegistrationAsync(AuditSqlScratch scratch, Seed seed)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        // Columns the seed leaves out take their database defaults (the statuses default to 1; the rest are NULL).
        var columns = new List<string> { "Id", "DisplayName", "Authority", "ClientId", "UsesDeploymentSecret", "ClientSecretRef", "CallbackPath", "Enabled", "IsActive", "CreatedUtc", "UpdatedUtc", "AzureTenantId", "AllowMemberAccessRequests", "AllowGuestAccessRequests" };
        var values = new List<string> { "@id", "@name", "@authority", "@client", "1", "@ref", "N'/signin-entra'", "1", "@active", "@created", "@updated", "@tenant", "1", "0" };
        command.Parameters.AddRange(new[]
        {
            P("@id", seed.Id), P("@name", "Registration " + seed.Id), P("@authority", Authority), P("@client", ClientId),
            P("@ref", Guid.NewGuid().ToString("D")), P("@active", seed.IsActive), P("@created", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            P("@updated", new DateTimeOffset(2026, 8, 9, 10, 11, 12, TimeSpan.Zero)), P("@tenant", TenantId),
        });

        void Add(string column, object? value)
        {
            if (value is null)
                return;
            columns.Add(column);
            values.Add("@" + column);
            command.Parameters.Add(P("@" + column, value));
        }

        Add("OpenIdConfigurationStatus", seed.OpenIdStatus);
        Add("LastOpenIdValidationUtc", seed.OpenIdUtc);
        Add("LastOpenIdValidationSummary", seed.OpenIdSummary);
        Add("LastOpenIdValidationDetail", seed.OpenIdDetail);
        Add("DirectoryDiscoveryStatus", seed.DirectoryStatus);
        Add("LastDirectoryDiscoveryUtc", seed.DirectoryUtc);
        Add("LastDirectoryDiscoverySummary", seed.DirectorySummary);
        Add("LastDirectoryDiscoveryDetail", seed.DirectoryDetail);

        command.CommandText = $"INSERT INTO EntraInstanceRegistrations ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)});";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ColumnNamesAsync(AuditSqlScratch scratch, string table) =>
        (await scratch.RowsAsync($"SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}');")).Select(r => (string)r["name"]!).ToList();

    private static async Task<Dictionary<string, Dictionary<string, object?>>> ConfigurationRowsAsync(AuditSqlScratch scratch) =>
        (await scratch.RowsAsync($"SELECT {ConfigurationColumns} FROM EntraInstanceRegistrations;")).ToDictionary(r => (string)r["Id"]!);

    /// <summary>The eight values by registration id, read from wherever they are stored in the given shape.</summary>
    private static async Task<Dictionary<string, Dictionary<string, object?>>> DiagnosticsAsync(AuditSqlScratch scratch, bool inOldColumns) =>
        (await scratch.RowsAsync(inOldColumns
            ? $"SELECT Id, {string.Join(", ", ProbeColumns)} FROM EntraInstanceRegistrations;"
            : $"SELECT RegistrationId AS Id, {string.Join(", ", ProbeColumns)} FROM EntraRegistrationDiagnostics;")).ToDictionary(r => (string)r["Id"]!);

    [Fact]
    public async Task Every_registration_keeps_all_eight_diagnostic_values_and_its_configuration_row_is_untouched()
    {
        await using var scratch = await ExistingInstallationAsync();
        var configurationBefore = await ConfigurationRowsAsync(scratch);
        var diagnosticsBefore = await DiagnosticsAsync(scratch, inOldColumns: true);
        diagnosticsBefore.Should().HaveCount(Seeds.Length);

        await scratch.MigrateToCurrentAsync();

        // The old columns are gone from the configuration row; the new table exists and holds one row per registration.
        (await ColumnNamesAsync(scratch, "EntraInstanceRegistrations")).Should().NotContain(ProbeColumns);
        var diagnosticsAfter = await DiagnosticsAsync(scratch, inOldColumns: false);
        diagnosticsAfter.Should().BeEquivalentTo(diagnosticsBefore, "all eight values of every registration are copied unchanged");

        // Spot checks that do not depend on the copy itself: the limits, the non-ASCII text, and the defaults of the never-probed row.
        ((string)diagnosticsAfter["mig-succeeded"]["LastOpenIdValidationDetail"]!).Should().Be(Detail4000).And.HaveLength(4000);
        ((string)diagnosticsAfter["mig-succeeded"]["LastOpenIdValidationSummary"]!).Should().Be(Summary512).And.HaveLength(512);
        diagnosticsAfter["mig-succeeded"]["LastOpenIdValidationUtc"].Should().Be(Validated);
        diagnosticsAfter["mig-never-probed"]["OpenIdConfigurationStatus"].Should().Be((int)OpenIdConfigurationStatus.NotValidated);
        diagnosticsAfter["mig-never-probed"]["DirectoryDiscoveryStatus"].Should().Be((int)DirectoryDiscoveryStatus.NotAttempted);
        diagnosticsAfter["mig-edge-statuses"]["OpenIdConfigurationStatus"].Should().Be((int)OpenIdConfigurationStatus.Unknown, "a stored 0 is not turned into the default");
        diagnosticsAfter["mig-directory-only"]["OpenIdConfigurationStatus"].Should().Be((int)OpenIdConfigurationStatus.NotValidated);

        // The configuration rows are the rows they were: every column, including UpdatedUtc and the rowversion an administrator's
        // open edit form carries across the upgrade.
        (await ConfigurationRowsAsync(scratch)).Should().BeEquivalentTo(configurationBefore);
    }

    [Fact]
    public async Task The_new_table_has_the_keys_lengths_nullability_and_defaults_the_columns_had()
    {
        await using var scratch = await ExistingInstallationAsync();
        await scratch.MigrateToCurrentAsync();

        var columns = (await scratch.RowsAsync("""
            SELECT c.name AS Name, t.name AS Type, c.max_length AS MaxLength, c.is_nullable AS IsNullable, dc.definition AS DefaultDefinition
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            WHERE c.object_id = OBJECT_ID(N'EntraRegistrationDiagnostics');
            """)).ToDictionary(r => (string)r["Name"]!);

        columns.Keys.Should().BeEquivalentTo(ProbeColumns.Append("RegistrationId"));
        void Expect(string name, string type, int maxLength, bool nullable, string? defaultDefinition = null)
        {
            columns[name]["Type"].Should().Be(type, name);
            ((short)columns[name]["MaxLength"]!).Should().Be((short)maxLength, name);
            columns[name]["IsNullable"].Should().Be(nullable, name);
            columns[name]["DefaultDefinition"].Should().Be(defaultDefinition, name);
        }

        Expect("RegistrationId", "nvarchar", 128, nullable: false);
        Expect("OpenIdConfigurationStatus", "int", 4, nullable: false, "((1))");
        Expect("DirectoryDiscoveryStatus", "int", 4, nullable: false, "((1))");
        Expect("LastOpenIdValidationUtc", "datetimeoffset", 10, nullable: true);
        Expect("LastDirectoryDiscoveryUtc", "datetimeoffset", 10, nullable: true);
        Expect("LastOpenIdValidationSummary", "nvarchar", 1024, nullable: true);
        Expect("LastDirectoryDiscoverySummary", "nvarchar", 1024, nullable: true);
        Expect("LastOpenIdValidationDetail", "nvarchar", 8000, nullable: true);
        Expect("LastDirectoryDiscoveryDetail", "nvarchar", 8000, nullable: true);

        // Keyed by the registration id, and removed with the registration.
        (await scratch.ScalarAsync<string>("""
            SELECT STRING_AGG(col.name, ',') FROM sys.key_constraints kc
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE kc.parent_object_id = OBJECT_ID(N'EntraRegistrationDiagnostics') AND kc.type = 'PK';
            """)).Should().Be("RegistrationId");
        (await scratch.RowsAsync("""
            SELECT delete_referential_action_desc AS OnDelete, OBJECT_NAME(referenced_object_id) AS Referenced
            FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'EntraRegistrationDiagnostics');
            """)).Should().ContainSingle().Which.Should().BeEquivalentTo(new Dictionary<string, object?> { ["OnDelete"] = "CASCADE", ["Referenced"] = "EntraInstanceRegistrations" });

        // A row that names only its registration takes the defaults.
        var fresh = await InsertRegistrationWithoutDiagnosticsAsync(scratch);
        await scratch.ExecuteAsync($"INSERT INTO EntraRegistrationDiagnostics (RegistrationId) VALUES (N'{fresh}');");
        var defaults = (await DiagnosticsRowAsync(scratch, fresh))!;
        defaults["OpenIdConfigurationStatus"].Should().Be(1);
        defaults["DirectoryDiscoveryStatus"].Should().Be(1);
        defaults.Where(c => c.Key is not ("OpenIdConfigurationStatus" or "DirectoryDiscoveryStatus")).Should().OnlyContain(c => c.Value == null);
    }

    [Fact]
    public async Task The_repository_the_registration_list_and_detail_and_the_overview_read_the_values_the_columns_held()
    {
        await using var scratch = await ExistingInstallationAsync();
        await scratch.MigrateToCurrentAsync();
        var admin = await AddAdministratorAsync(scratch);

        await using var db = scratch.NewDb();
        var repository = new SqlEntraInstanceRegistrationRepository(db);

        void AssertMatchesSeed(EntraInstanceRegistration read, Seed seed)
        {
            read.Id.Should().Be(seed.Id);
            read.OpenIdConfigurationStatus.Should().Be((OpenIdConfigurationStatus)(seed.OpenIdStatus ?? (int)OpenIdConfigurationStatus.NotValidated), seed.Id);
            read.LastOpenIdValidationUtc.Should().Be(seed.OpenIdUtc, seed.Id);
            read.LastOpenIdValidationSummary.Should().Be(seed.OpenIdSummary, seed.Id);
            read.LastOpenIdValidationDetail.Should().Be(seed.OpenIdDetail, seed.Id);
            read.DirectoryDiscoveryStatus.Should().Be((DirectoryDiscoveryStatus)(seed.DirectoryStatus ?? (int)DirectoryDiscoveryStatus.NotAttempted), seed.Id);
            read.LastDirectoryDiscoveryUtc.Should().Be(seed.DirectoryUtc, seed.Id);
            read.LastDirectoryDiscoverySummary.Should().Be(seed.DirectorySummary, seed.Id);
            read.LastDirectoryDiscoveryDetail.Should().Be(seed.DirectoryDetail, seed.Id);
        }

        var listed = await repository.ListAsync();
        listed.Should().HaveCount(Seeds.Length);
        foreach (var seed in Seeds)
        {
            AssertMatchesSeed(listed.Single(r => r.Id == seed.Id), seed);
            AssertMatchesSeed((await repository.GetByIdAsync(seed.Id))!, seed);
        }

        var active = Seeds.Single(s => s.IsActive);
        AssertMatchesSeed((await repository.GetActiveAsync())!, active);

        // The controller's list and detail show the same diagnostic fields for the same registration.
        var controller = Controller(scratch, db, admin);
        var list = (IReadOnlyList<EntraTenantListItemResponse>)((OkObjectResult)(await controller.List(CancellationToken.None)).Result!).Value!;
        foreach (var seed in Seeds)
        {
            var detail = (EntraTenantListItemResponse)((OkObjectResult)(await controller.GetById(seed.Id, CancellationToken.None)).Result!).Value!;
            var item = list.Single(r => r.Id == seed.Id);
            item.Should().BeEquivalentTo(detail, $"the list and the detail of {seed.Id} show the same registration");
            item.OpenIdConfigurationStatus.Should().Be(((OpenIdConfigurationStatus)(seed.OpenIdStatus ?? 1)).ToString());
            item.DirectoryDiscoveryStatus.Should().Be(((DirectoryDiscoveryStatus)(seed.DirectoryStatus ?? 1)).ToString());
            item.LastOpenIdValidationUtc.Should().Be(seed.OpenIdUtc);
            item.LastOpenIdValidationSummary.Should().Be(seed.OpenIdSummary);
            item.LastOpenIdValidationDetail.Should().Be(seed.OpenIdDetail);
            item.LastDirectoryDiscoveryUtc.Should().Be(seed.DirectoryUtc);
            item.LastDirectoryDiscoverySummary.Should().Be(seed.DirectorySummary);
            item.LastDirectoryDiscoveryDetail.Should().Be(seed.DirectoryDetail);
        }

        // The authentication overview reports the active registration's statuses.
        var overview = await new AuthenticationAdminOverviewService(
            new SqlInstanceAuthUiPolicyRepository(db),
            repository,
            new ConfigurationBuilder().Build(),
            Options.Create(new LocalAuthOptions { Enabled = true }),
            Options.Create(new EntraAuthOptions())).GetAsync();
        overview.ActiveEntraRegistrationId.Should().Be(active.Id);
        overview.AuthProviderAvailability.ActiveEntraOpenIdConfigurationStatus.Should().Be(nameof(OpenIdConfigurationStatus.Valid));
        overview.AuthProviderAvailability.ActiveEntraDirectoryDiscoveryStatus.Should().Be(nameof(DirectoryDiscoveryStatus.Success));
    }

    [Fact]
    public async Task After_the_upgrade_a_new_registration_gets_its_diagnostics_row_and_deleting_one_removes_its_row()
    {
        await using var scratch = await ExistingInstallationAsync();
        await scratch.MigrateToCurrentAsync();
        var admin = await AddAdministratorAsync(scratch);

        var created = await CreateRegistrationAsync(scratch, admin, "Created after the upgrade");
        (await DiagnosticsRowCountAsync(scratch, created)).Should().Be(1);

        // A migrated, never-probed registration is deleted with the row the migration made for it.
        (await DiagnosticsRowCountAsync(scratch, "mig-never-probed")).Should().Be(1);
        await using (var db = scratch.NewDb())
            await new SqlEntraInstanceRegistrationRepository(db).DeleteAsync("mig-never-probed");
        (await DiagnosticsRowCountAsync(scratch, "mig-never-probed")).Should().Be(0);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM EntraRegistrationDiagnostics;")).Should().Be(Seeds.Length, "the seeded registrations plus the created one, minus the deleted one");

        // A migrated registration is probed and edited like any other.
        var form = await GetAsync(scratch, "mig-failed", admin);
        await using (var db = scratch.NewDb())
            (await Controller(scratch, db, admin).ValidateOpenId("mig-failed", CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        await using (var db = scratch.NewDb())
            (await Controller(scratch, db, admin).Patch("mig-failed", UpdateBody(form, displayName: "Renamed after the upgrade"), CancellationToken.None))
                .Result.Should().BeOfType<OkObjectResult>("the probe did not invalidate the form's rowversion");
        (await DiagnosticsRowAsync(scratch, "mig-failed"))!["OpenIdConfigurationStatus"].Should().Be((int)OpenIdConfigurationStatus.Valid);
    }

    [Fact]
    public async Task Rolling_back_restores_the_columns_with_their_data_and_the_upgrade_can_be_applied_again()
    {
        await using var scratch = await ExistingInstallationAsync();
        var configurationBefore = await ConfigurationRowsAsync(scratch);
        var diagnosticsBefore = await DiagnosticsAsync(scratch, inOldColumns: true);

        await scratch.MigrateToCurrentAsync();
        // Telemetry recorded after the upgrade is carried back by the rollback.
        await using (var db = scratch.NewDb())
            await new SqlEntraInstanceRegistrationRepository(db).UpdateOpenIdValidationOutcomeAsync(
                "mig-never-probed", OpenIdConfigurationStatus.Valid, Validated, "recorded after the upgrade", null);

        await scratch.MigrateToAsync(PreMigration);

        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = N'EntraRegistrationDiagnostics';")).Should().Be(0);
        var columns = (await scratch.RowsAsync("""
            SELECT c.name AS Name, c.is_nullable AS IsNullable, dc.definition AS DefaultDefinition
            FROM sys.columns c LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            WHERE c.object_id = OBJECT_ID(N'EntraInstanceRegistrations');
            """)).ToDictionary(r => (string)r["Name"]!);
        columns.Keys.Should().Contain(ProbeColumns);
        columns["OpenIdConfigurationStatus"]["DefaultDefinition"].Should().Be("((1))");
        columns["DirectoryDiscoveryStatus"]["DefaultDefinition"].Should().Be("((1))");
        columns["OpenIdConfigurationStatus"]["IsNullable"].Should().Be(false);

        var diagnosticsRolledBack = await DiagnosticsAsync(scratch, inOldColumns: true);
        diagnosticsRolledBack["mig-never-probed"]["OpenIdConfigurationStatus"].Should().Be((int)OpenIdConfigurationStatus.Valid);
        diagnosticsRolledBack["mig-never-probed"]["LastOpenIdValidationSummary"].Should().Be("recorded after the upgrade");
        foreach (var seed in Seeds.Where(s => s.Id != "mig-never-probed"))
            diagnosticsRolledBack[seed.Id].Should().BeEquivalentTo(diagnosticsBefore[seed.Id], seed.Id);

        // The configuration columns and UpdatedUtc are untouched by the round trip (the rowversion is replaced by the copy-back).
        var configurationRolledBack = await ConfigurationRowsAsync(scratch);
        foreach (var (id, before) in configurationBefore)
            configurationRolledBack[id].Where(c => c.Key != "RowVersion").Should().BeEquivalentTo(before.Where(c => c.Key != "RowVersion"), id);

        await scratch.MigrateToCurrentAsync();
        (await DiagnosticsAsync(scratch, inOldColumns: false))["mig-never-probed"]["LastOpenIdValidationSummary"].Should().Be("recorded after the upgrade");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM EntraRegistrationDiagnostics;")).Should().Be(Seeds.Length);
    }
}
