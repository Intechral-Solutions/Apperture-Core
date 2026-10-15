using System.Text.Json;
using System.Text.RegularExpressions;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.SettingSecretOwnershipSqlSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// SEC-2.3 (A3.7): the read-only inventory of setting secret references the platform now refuses
/// (<c>scripts/sec2/Get-SettingSecretOwnershipInventory.sql</c>), run against a real catalog seeded with every class on locking
/// READ COMMITTED and READ_COMMITTED_SNAPSHOT. It returns exactly the seeded problem rows with the right class, nothing for a clean
/// or empty catalog, nothing that identifies a secret, and it works, and writes nothing, as a database user that holds SELECT on the
/// five tables it reads and nothing else. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SettingSecretOwnershipInventorySqlServerTests
{
    private const string ScriptPath = "scripts/sec2/Get-SettingSecretOwnershipInventory.sql";
    private const string ReaderUser = "sec23_inventory_reader";
    private const string ListedKey = "Sec23.Api.Token";
    private const string OtherListedKey = "Sec23.Other.Token";
    private const string UnlistedKey = "Sec23.Ordinary.Text";
    private const string ModuleKeyComment = "-- , (N'<module setting key>')";

    /// <summary>The sensitive keys the operator lists: the two the cases use, the keys that carry the delimiter and the escape character, and one that is only ever clean.</summary>
    private static readonly string[] Listed = [ListedKey, OtherListedKey, "Key:With:Colons", "Key%25", "c", "q:r", "Sec23.Escaped:Key"];

    private readonly ITestOutputHelper _output;

    public SettingSecretOwnershipInventorySqlServerTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Isolation() => SettingSecretOwnershipSqlSupport.Isolation();

    // ---- the script, as an operator edits it ----

    private static string RawScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the repository root holds AGENTS.md");
        return File.ReadAllText(Path.Combine(dir!.FullName, ScriptPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>The script with the module's sensitive keys added to the list, which is the one edit an operator makes.</summary>
    private static string ScriptWith(params string[] moduleKeys)
    {
        var script = RawScript();
        script.Should().Contain(ModuleKeyComment, "the list says where a module's keys go");
        return script.Replace(ModuleKeyComment, string.Concat(moduleKeys.Select(k => $", (N'{k}')")));
    }

    private sealed record Finding(
        string ScopeKind, string ScopeId, string SettingKey, string Class, bool Listed, string Relation, int? SoleHolder,
        int? OtherSettingRows, int? OtherConnections, int? OtherEntra, string? ForeignProvider, string? ForeignOwnerName);

    private static readonly string[] ExpectedColumns =
    [
        "ScopeKind", "ScopeId", "SettingKey", "Class", "KeyListedSensitive", "Relation", "SoleHolder",
        "OtherSettingRows", "OtherConnections", "OtherEntraRegistrations", "ForeignProvider", "ForeignOwnerName",
    ];

    private static async Task<(List<string> Columns, List<Finding> Rows)> RunAsync(AuditSqlScratch scratch, string script, string? asUser = null)
    {
        await using var connection = new SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        if (asUser is not null)
        {
            await using var impersonate = connection.CreateCommand();
            impersonate.CommandText = $"EXECUTE AS USER = N'{asUser}';";
            await impersonate.ExecuteNonQueryAsync();
        }

        var columns = new List<string>();
        var rows = new List<Finding>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = script;
            await using var reader = await command.ExecuteReaderAsync();
            for (var i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));
            while (await reader.ReadAsync())
            {
                string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                int? Number(int i) => reader.IsDBNull(i) ? null : Convert.ToInt32(reader.GetValue(i));
                rows.Add(new Finding(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    Convert.ToInt32(reader.GetValue(4)) == 1, reader.GetString(5), Number(6), Number(7), Number(8), Number(9), Text(10), Text(11)));
            }
        }

        if (asUser is not null)
        {
            await using var revert = connection.CreateCommand();
            revert.CommandText = "REVERT;";
            await revert.ExecuteNonQueryAsync();
        }

        return (columns, rows);
    }

    // ---- seeding ----

    private sealed record Seeded(string ScopeKind, string ScopeId, string Key, string Class, string Reference);

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static async Task DisableAsync(AuditSqlScratch scratch, string secretRef)
    {
        await using var db = scratch.NewDb();
        await ConnectionSqlHarness.SecretStore(db).DisableAsync(new SecretId(Guid.Parse(secretRef)));
    }

    /// <summary>Every class the script must name, plus rows it must stay silent about.</summary>
    private static async Task<(List<Seeded> Problems, List<(string Kind, string Scope, string Key)> Clean)> SeedEverythingAsync(AuditSqlScratch scratch)
    {
        var problems = new List<Seeded>();
        var clean = new List<(string, string, string)>();

        async Task<Seeded> Integration(string klass, string integration, string key, string reference)
        {
            await InsertIntegrationRowAsync(scratch, integration, key, reference);
            var seeded = new Seeded(PlatformSettingScopeConstants.IntegrationKind, integration, key, klass, reference);
            if (klass != "owned")
                problems.Add(seeded);
            else
                clean.Add((seeded.ScopeKind, integration, key));
            return seeded;
        }

        // Clean: the setting's own secret, instance and integration.
        var cleanId = "int-clean-" + Suffix();
        await Integration("owned", cleanId, ListedKey, await SeedOwnedAsync(scratch, cleanId, ListedKey));
        await InsertInstanceRowAsync(scratch, SmtpKey, await ConnectionSqlHarness.SeedSecretAsync(
            scratch, SettingSecretOwnership.PlatformProvider, SettingSecretOwnership.ForPlatformSetting(SmtpKey).Name, OwnMaterial));
        clean.Add((PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, SmtpKey));

        // Clean: ids and keys that hold the delimiter and the escape character. The script's escaping must equal the platform's.
        foreach (var (id, key) in new[] { ("int:colon:" + Suffix(), "Key:With:Colons"), ("int%percent%" + Suffix(), "Key%25"), ("a:b", "c"), ("p", "q:r") })
            await Integration("owned", id, key, await SeedOwnedAsync(scratch, id, key));

        // legacy_integration: sole holder, shared with another integration, held also by a connection and by an Entra registration.
        var soleId = "int-legacy-sole-" + Suffix();
        var soleRef = await SeedLegacyAsync(scratch, ListedKey);
        await Integration("legacy_integration", soleId, ListedKey, soleRef);

        var sharedA = "int-legacy-a-" + Suffix();
        var sharedB = "int-legacy-b-" + Suffix();
        var sharedRef = await SeedLegacyAsync(scratch, OtherListedKey);
        await Integration("legacy_integration", sharedA, OtherListedKey, sharedRef);
        await Integration("legacy_integration", sharedB, OtherListedKey, sharedRef);

        var withConnection = "int-legacy-conn-" + Suffix();
        var connectionRef = await SeedLegacyAsync(scratch, ListedKey);
        await Integration("legacy_integration", withConnection, ListedKey, connectionRef);
        await InsertConnectionAsync(scratch, "con-" + Suffix(), JsonSerializer.Serialize(new Dictionary<string, string> { ["Password"] = connectionRef }));

        var withEntra = "int-legacy-entra-" + Suffix();
        var entraHeld = await SeedLegacyAsync(scratch, ListedKey);
        await Integration("legacy_integration", withEntra, ListedKey, entraHeld);
        await InsertEntraAsync(scratch, "reg-" + Suffix(), entraHeld.ToUpperInvariant().Replace("-", string.Empty));

        // key_mismatch: another key of the same integration, another integration's secret, a secret made for the instance setting of
        // another key, a case variant and a trailing-space variant of the owner's name.
        var mismatchId = "int-mismatch-" + Suffix();
        await Integration("key_mismatch", mismatchId, ListedKey, await SeedForeignAsync(scratch, Foreign.OtherKeySameIntegration, mismatchId, ListedKey));
        var otherIntegration = "int-mismatch-b-" + Suffix();
        await Integration("key_mismatch", otherIntegration, ListedKey, await SeedForeignAsync(scratch, Foreign.OtherIntegration, otherIntegration, ListedKey));
        var caseId = "int-case-" + Suffix();
        await Integration("key_mismatch", caseId, ListedKey, await SeedForeignAsync(scratch, Foreign.CaseVariant, caseId, ListedKey));
        var paddedId = "int-padded-" + Suffix();
        await Integration("key_mismatch", paddedId, ListedKey, await ConnectionSqlHarness.SeedSecretAsync(
            scratch, SettingSecretOwnership.IntegrationProvider, SettingSecretOwnership.ForIntegrationSetting(paddedId, ListedKey).Name + "   ", ForeignMaterial));

        // foreign_provider: a connection's secret and an Entra registration's secret (an incident).
        var providerA = "int-foreign-con-" + Suffix();
        await Integration("foreign_provider", providerA, ListedKey, await SeedForeignAsync(scratch, Foreign.Connection, providerA, ListedKey));
        var providerB = "int-foreign-entra-" + Suffix();
        await Integration("foreign_provider", providerB, ListedKey, await SeedForeignAsync(scratch, Foreign.Entra, providerB, ListedKey));
        var providerCase = "int-foreign-case-" + Suffix();
        await Integration("foreign_provider", providerCase, ListedKey, await ConnectionSqlHarness.SeedSecretAsync(
            scratch, "Integration-Setting", SettingSecretOwnership.ForIntegrationSetting(providerCase, ListedKey).Name, ForeignMaterial));

        // A foreign reference written in every other spelling the platform accepts is still the same secret.
        foreach (var make in new Func<string, string>[] { g => g.Replace("-", string.Empty), g => "{" + g.ToUpperInvariant() + "}", g => "(" + g + ")", g => "  " + g + "  " })
        {
            var spelled = "int-spelled-" + Suffix();
            var reference = await SeedForeignAsync(scratch, Foreign.Connection, spelled, ListedKey);
            await Integration("foreign_provider", spelled, ListedKey, make(reference));
        }

        // An instance row that names an integration's secret: a mismatch.
        var integrationSecretForInstance = await SeedOwnedAsync(scratch, "int-source-" + Suffix(), OtherListedKey, ForeignMaterial);
        await InsertRowAsync(
            scratch, PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, OtherListedKey, JsonSerializer.Serialize(integrationSecretForInstance));
        problems.Add(new Seeded(PlatformSettingScopeConstants.InstanceKind, PlatformSettingScopeConstants.InstanceId, OtherListedKey, "key_mismatch", integrationSecretForInstance));

        // shared: the owner's row, with a poisoned row elsewhere that also names its secret (that row is a mismatch).
        var sharedOwner = "int-owner-" + Suffix();
        var ownedRef = await SeedOwnedAsync(scratch, sharedOwner, ListedKey);
        await Integration("shared", sharedOwner, ListedKey, ownedRef);
        problems[^1] = problems[^1] with { Class = "shared" };
        var poisoned = "int-poisoned-" + Suffix();
        await Integration("key_mismatch", poisoned, ListedKey, ownedRef);

        // ordinary_guid_text: a key that is not listed holds text that names a usable secret.
        var ordinaryId = "int-ord-legacy-" + Suffix();
        await Integration("ordinary_guid_text", ordinaryId, UnlistedKey, await SeedLegacyAsync(scratch, UnlistedKey));
        var ordinaryOwned = "int-ord-owned-" + Suffix();
        await Integration("ordinary_guid_text", ordinaryOwned, UnlistedKey, await SeedOwnedAsync(scratch, ordinaryOwned, UnlistedKey));

        // dangling: a listed key that names no secret, a disabled secret of its own, a secret with no active version.
        var dangling = "int-dangling-" + Suffix();
        await Integration("dangling", dangling, ListedKey, Guid.NewGuid().ToString("D"));
        var disabledId = "int-disabled-" + Suffix();
        var disabled = await SeedOwnedAsync(scratch, disabledId, ListedKey);
        await DisableAsync(scratch, disabled);
        await Integration("dangling", disabledId, ListedKey, disabled);
        var pendingId = "int-pending-" + Suffix();
        await using (var db = scratch.NewDb())
        {
            var store = ConnectionSqlHarness.SecretStore(db);
            var descriptor = await store.CreateAsync(new SecretCreateRequest(
                SettingSecretOwnership.ForIntegrationSetting(pendingId, ListedKey).Name, SettingSecretOwnership.IntegrationProvider, SecretMaterialType.OpaqueString));
            await store.AddVersionAsync(descriptor.SecretId, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: OwnMaterial));
            await Integration("dangling", pendingId, ListedKey, descriptor.SecretId.Value.ToString("D"));
        }

        // unreadable: a listed key whose value is not one readable reference.
        foreach (var (label, json) in new[] { ("text", "\"not-a-guid\""), ("blank", "\"\""), ("number", "5"), ("null", "null"), ("array", "[\"x\"]"), ("text after guid", "\"" + Guid.NewGuid() + "junk\"") })
        {
            var id = "int-unreadable-" + label.Replace(' ', '-') + "-" + Suffix();
            await InsertRowAsync(scratch, PlatformSettingScopeConstants.IntegrationKind, id, ListedKey, json);
            problems.Add(new Seeded(PlatformSettingScopeConstants.IntegrationKind, id, ListedKey, "unreadable", json));
        }

        // Ordinary values the script must not name: text, a number, a GUID that names no secret, on a key that is not listed.
        foreach (var json in new[] { "\"just a value\"", "30", "\"" + Guid.NewGuid() + "\"", "true", "[\"a\",\"b\"]" })
        {
            var id = "int-ordinary-value-" + Suffix();
            await InsertRowAsync(scratch, PlatformSettingScopeConstants.IntegrationKind, id, UnlistedKey, json);
            clean.Add((PlatformSettingScopeConstants.IntegrationKind, id, UnlistedKey));
        }

        return (problems, clean);
    }

    // ---- A3.7 ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_script_returns_exactly_the_seeded_problem_rows_with_the_right_class_and_nothing_that_identifies_a_secret(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Sec23InvA_", rcsi);
        var (problems, clean) = await SeedEverythingAsync(scratch);

        var (columns, rows) = await RunAsync(scratch, ScriptWith(Listed));

        columns.Should().Equal(ExpectedColumns);
        rows.Select(r => (r.ScopeKind, r.ScopeId, r.SettingKey, r.Class)).Should().BeEquivalentTo(
            problems.Select(p => (p.ScopeKind, p.ScopeId, p.Key, p.Class)),
            "exactly the seeded problems, each once, with the class the platform's own rule gives it");
        foreach (var (kind, scope, key) in clean)
            rows.Should().NotContain(r => r.ScopeKind == kind && r.ScopeId == scope && r.SettingKey == key, $"{scope}/{key} names what it owns, or holds an ordinary value");

        // The H1 evidence the operator needs: who else holds a legacy secret.
        Finding Row(string idPrefix) => rows.Single(r => r.ScopeId.StartsWith(idPrefix, StringComparison.Ordinal));
        Row("int-legacy-sole-").SoleHolder.Should().Be(1);
        rows.Where(r => r.ScopeId.StartsWith("int-legacy-a-") || r.ScopeId.StartsWith("int-legacy-b-")).Should().OnlyContain(r => r.SoleHolder == 0 && r.OtherSettingRows == 1);
        Row("int-legacy-conn-").OtherConnections.Should().Be(1);
        Row("int-legacy-conn-").SoleHolder.Should().Be(0);
        Row("int-legacy-entra-").OtherEntra.Should().Be(1, "a 32-digit upper-case spelling is the same reference");
        Row("int-foreign-con-").ForeignProvider.Should().Be("platform-connection");
        Row("int-foreign-con-").ForeignOwnerName.Should().StartWith("connection:");
        Row("int-foreign-entra-").ForeignProvider.Should().Be("entra-registration");
        Row("int-mismatch-b-").ForeignProvider.Should().Be("integration-setting");
        Row("int-mismatch-b-").ForeignOwnerName.Should().StartWith("integration-setting:").And.EndWith($":{ListedKey}")
            .And.NotContain("int-mismatch-b-", "it names the other integration, whose credential may have been used (an owner, not a secret)");
        Row("int-owner-").Class.Should().Be("shared");
        Row("int-owner-").OtherSettingRows.Should().Be(1);
        Row("int-ord-owned-").Relation.Should().Be("owned");
        Row("int-ord-legacy-").Relation.Should().NotBeNullOrEmpty();
        rows.Where(r => r.Class == "ordinary_guid_text").Should().OnlyContain(r => !r.Listed);
        rows.Where(r => r.Class is "dangling" or "unreadable").Should().OnlyContain(r => r.Listed);

        // Nothing in any cell identifies a secret: no reference in any spelling, no secret id, no material.
        var everything = string.Join("\n", rows.Select(r => string.Join("|", r.ScopeKind, r.ScopeId, r.SettingKey, r.Class, r.Relation, r.ForeignProvider, r.ForeignOwnerName)));
        foreach (var problem in problems.Where(p => Guid.TryParse(p.Reference.Trim(), out _)))
        {
            var guid = Guid.Parse(problem.Reference.Trim());
            everything.Should().NotContainEquivalentOf(guid.ToString("D"));
            everything.Should().NotContainEquivalentOf(guid.ToString("N"));
        }

        everything.Should().NotContain(ForeignMaterial).And.NotContain(OwnMaterial);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_clean_catalog_and_an_empty_one_return_nothing(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Sec23InvB_", rcsi);
        (await RunAsync(scratch, ScriptWith(Listed))).Rows.Should().BeEmpty("an empty catalog has nothing to list");

        foreach (var key in new[] { ListedKey, OtherListedKey, "Sec23.Escaped:Key" })
        {
            var id = "int-clean-" + Suffix();
            await InsertIntegrationRowAsync(scratch, id, key, await SeedOwnedAsync(scratch, id, key));
        }

        await InsertInstanceRowAsync(scratch, SmtpKey, await ConnectionSqlHarness.SeedSecretAsync(
            scratch, SettingSecretOwnership.PlatformProvider, SettingSecretOwnership.ForPlatformSetting(SmtpKey).Name, OwnMaterial));
        await InsertRowAsync(scratch, PlatformSettingScopeConstants.IntegrationKind, "int-plain-" + Suffix(), UnlistedKey, "\"a value\"");

        (await RunAsync(scratch, ScriptWith(Listed))).Rows.Should().BeEmpty("every setting names the secret it owns, or holds an ordinary value");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_script_runs_as_a_user_that_can_only_read_the_five_tables_changes_nothing_and_is_repeatable(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Sec23InvC_", rcsi);
        var (problems, _) = await SeedEverythingAsync(scratch);
        await scratch.ExecuteAsync(
            $"""
            CREATE USER [{ReaderUser}] WITHOUT LOGIN;
            GRANT SELECT ON dbo.PlatformSettingValues TO [{ReaderUser}];
            GRANT SELECT ON dbo.SecretDescriptors TO [{ReaderUser}];
            GRANT SELECT ON dbo.SecretVersions TO [{ReaderUser}];
            GRANT SELECT ON dbo.Connections TO [{ReaderUser}];
            GRANT SELECT ON dbo.EntraInstanceRegistrations TO [{ReaderUser}];
            """);
        var script = ScriptWith(Listed);
        var asOwner = (await RunAsync(scratch, script)).Rows;
        var before = await SettingsSnapshotAsync(scratch);

        var asReader = await RunAsync(scratch, script, ReaderUser);
        var again = await RunAsync(scratch, script, ReaderUser);

        asReader.Rows.Should().BeEquivalentTo(asOwner, "the read-only user sees the same catalog");
        asReader.Rows.Should().HaveCount(problems.Count);
        again.Rows.Should().Equal(asReader.Rows);
        (await SettingsSnapshotAsync(scratch)).Should().Be(before, "the script wrote nothing");

        // The controls that make the above meaningful: this user really is limited to reading those tables.
        await using var connection = new SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using (var impersonate = connection.CreateCommand())
        {
            impersonate.CommandText = $"EXECUTE AS USER = N'{ReaderUser}';";
            await impersonate.ExecuteNonQueryAsync();
        }

        async Task<int> ErrorNumberOf(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var ex = await Assert.ThrowsAsync<SqlException>(async () => await command.ExecuteNonQueryAsync());
            return ex.Number;
        }

        (await ErrorNumberOf("UPDATE dbo.PlatformSettingValues SET ValueJson = N'\"changed\"';")).Should().Be(229, "no permission to write");
        (await ErrorNumberOf("SELECT COUNT(*) FROM dbo.Users;")).Should().Be(229, "no permission to read anything else");
        (await ErrorNumberOf("DELETE FROM dbo.SecretVersions;")).Should().Be(229);
    }

    private static async Task<string> SettingsSnapshotAsync(AuditSqlScratch scratch)
    {
        static string Text(IEnumerable<Dictionary<string, object?>> rows) =>
            string.Join("|", rows.Select(r => string.Join(",", r.Values.Select(v => v is byte[] b ? Convert.ToBase64String(b) : v?.ToString()))));

        return Text(await scratch.RowsAsync("SELECT * FROM PlatformSettingValues ORDER BY Id;"))
            + "#" + Text(await scratch.RowsAsync("SELECT * FROM SecretDescriptors ORDER BY Id;"))
            + "#" + Text(await scratch.RowsAsync("SELECT SecretId, Version FROM SecretVersions ORDER BY SecretId, Version;"))
            + "#" + await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;");
    }

    // ---- what the script is ----

    [Fact]
    public void The_script_has_no_statement_that_writes_creates_or_executes_and_never_reads_the_payload()
    {
        var code = Regex.Replace(Regex.Replace(RawScript(), @"/\*.*?\*/", " ", RegexOptions.Singleline), @"--[^\r\n]*", " ");

        Regex.IsMatch(code, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|GRANT|REVOKE|DENY|EXEC|EXECUTE|BULK|INTO|OPENROWSET|OPENQUERY|WAITFOR|DBCC|BACKUP|RESTORE|SHUTDOWN|DECLARE)\b", RegexOptions.IgnoreCase)
            .Should().BeFalse("the inventory is read-only and keeps no state, not even a table variable");
        Regex.IsMatch(code, @"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline).Should().BeFalse("it runs as one batch");
        code.Should().NotContain("#", "no temporary table");
        code.Should().NotContainEquivalentOf("ProtectedPayload").And.NotContainEquivalentOf("KeyId", "no secret material is ever selected");
        code.Should().Contain("SELECT").And.Contain("OPENJSON");
    }

    [Fact]
    public void The_script_names_no_database_and_no_installation()
    {
        var script = RawScript();

        Regex.IsMatch(script, @"\bUSE\s+\[?\w", RegexOptions.IgnoreCase).Should().BeFalse("it runs against whichever database the operator connects to");
        script.Should().NotContain("Apperture_", "no database name is invented");
    }
}
