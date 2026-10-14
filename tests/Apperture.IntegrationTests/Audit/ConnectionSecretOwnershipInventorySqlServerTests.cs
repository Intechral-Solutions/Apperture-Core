using System.Text.RegularExpressions;
using Apperture.Core.Domain.Connector;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.ConnectionSecretOwnershipSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// SEC-2.2 (A2.6): the read-only inventory of connection secret references that the platform now refuses
/// (<c>scripts/sec2/Get-ConnectionSecretOwnershipInventory.sql</c>), run against a real catalog seeded with every class on locking
/// READ COMMITTED and READ_COMMITTED_SNAPSHOT. It returns exactly the seeded problem rows with the right class, nothing for a clean
/// or empty catalog, nothing that identifies a secret, and it works, and writes nothing, as a database user that holds SELECT on
/// the three tables it reads and nothing else. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ConnectionSecretOwnershipInventorySqlServerTests
{
    private const string ScriptPath = "scripts/sec2/Get-ConnectionSecretOwnershipInventory.sql";
    private const string ReaderUser = "sec22_inventory_reader";

    private readonly ITestOutputHelper _output;

    public ConnectionSecretOwnershipInventorySqlServerTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    // ---- the script ----

    private static string ScriptText()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the repository root holds AGENTS.md");
        return File.ReadAllText(Path.Combine(dir!.FullName, ScriptPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private sealed record Finding(string ConnectionId, string? FieldKey, string Class, string State, string? ForeignProvider, string? ForeignOwnerName, string ConnectionName);

    private static readonly string[] ExpectedColumns =
        ["ConnectionId", "ConnectionName", "ConnectionState", "FieldKey", "Class", "ForeignProvider", "ForeignOwnerName"];

    private static async Task<(List<string> Columns, List<Finding> Rows)> RunAsync(AuditSqlScratch scratch, string? asUser = null)
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
            command.CommandText = ScriptText();
            await using var reader = await command.ExecuteReaderAsync();
            for (var i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));
            while (await reader.ReadAsync())
            {
                rows.Add(new Finding(
                    reader.GetString(0),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(2),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(1)));
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

    private sealed record Seeded(string ConnectionId, string Field, string Expected, string Reference);

    /// <summary>Adds a connection whose single field holds the reference that <paramref name="kind"/> produces.</summary>
    private static async Task<Seeded> SeedConnectionAsync(AuditSqlScratch scratch, Kind kind, string expected, string field = Field, ConnectionState state = ConnectionState.Active)
    {
        var connectionId = NewConnectionId();
        var reference = await SeedReferenceAsync(scratch, connectionId, kind, field);
        await AddConnectionAsync(scratch, connectionId, new Dictionary<string, string> { [field] = reference }, state);
        return new Seeded(connectionId, field, expected, reference);
    }

    private static async Task AddConnectionAsync(AuditSqlScratch scratch, string connectionId, Dictionary<string, string> refs, ConnectionState state = ConnectionState.Active)
    {
        await using var db = scratch.NewDb();
        await new SqlConnectionRepository(db).AddAsync(new Connection(
            new ConnectionId(connectionId), ConnectionSqlHarness.Sql, $"Connection {connectionId[^6..]}",
            new Dictionary<string, string> { ["Host"] = "h" }, refs, ScopeKey: null, OwnerId: null, State: state));
    }

    private static async Task SetRawRefsAsync(AuditSqlScratch scratch, string connectionId, string json) =>
        await scratch.ExecuteAsync($"UPDATE Connections SET SecretRefsJson = N'{json.Replace("'", "''")}' WHERE Id = N'{connectionId}';");

    /// <summary>Every class the script must name, plus connections it must stay silent about.</summary>
    private static async Task<(List<Seeded> Problems, List<string> Clean)> SeedEverythingAsync(AuditSqlScratch scratch)
    {
        var problems = new List<Seeded>
        {
            await SeedConnectionAsync(scratch, Kind.Dangling, "dangling"),
            await SeedConnectionAsync(scratch, Kind.DisabledOwned, "dangling"),
            await SeedConnectionAsync(scratch, Kind.PendingOnly, "dangling"),
            await SeedConnectionAsync(scratch, Kind.Unreadable, "unreadable"),
            await SeedConnectionAsync(scratch, Kind.KeyVaultStyle, "unreadable"),
            await SeedConnectionAsync(scratch, Kind.Blank, "unreadable"),
            await SeedConnectionAsync(scratch, Kind.OtherProvider, "foreign_provider"),
            await SeedConnectionAsync(scratch, Kind.ProviderCaseVariant, "foreign_provider"),
            await SeedConnectionAsync(scratch, Kind.OtherConnection, "foreign_owner"),
            await SeedConnectionAsync(scratch, Kind.OtherField, "foreign_owner"),
            await SeedConnectionAsync(scratch, Kind.NameCaseVariant, "foreign_owner"),
            await SeedConnectionAsync(scratch, Kind.OtherConnection, "foreign_owner", state: ConnectionState.Retired),
        };

        // A non-string and a null value in an otherwise readable map.
        var nonString = await SeedConnectionAsync(scratch, Kind.Owned, "unreadable");
        await SetRawRefsAsync(scratch, nonString.ConnectionId, "{\"Password\":5}");
        problems.Add(nonString with { Reference = "5" });
        var nullValue = await SeedConnectionAsync(scratch, Kind.Owned, "unreadable");
        await SetRawRefsAsync(scratch, nullValue.ConnectionId, "{\"Password\":null}");
        problems.Add(nullValue with { Reference = "null" });

        // A reference written in the other spellings .NET accepts for a GUID resolves to the same secret: still foreign.
        var spelled = await SeedConnectionAsync(scratch, Kind.OtherConnection, "foreign_owner");
        await SetRawRefsAsync(scratch, spelled.ConnectionId, "{\"Password\":\"{" + spelled.Reference.ToUpperInvariant() + "}\"}");
        problems.Add(spelled);

        // Several fields: only the poisoned one is listed.
        var mixedId = NewConnectionId();
        var own = await SeedReferenceAsync(scratch, mixedId, Kind.Owned, "Password");
        var foreign = await SeedReferenceAsync(scratch, mixedId, Kind.OtherConnection, "ApiKey");
        await AddConnectionAsync(scratch, mixedId, new Dictionary<string, string> { ["Password"] = own, ["ApiKey"] = foreign });
        problems.Add(new Seeded(mixedId, "ApiKey", "foreign_owner", foreign));

        // A map that is not an object at all.
        foreach (var raw in new[] { "this is not json", "[]" })
        {
            var broken = NewConnectionId();
            await AddConnectionAsync(scratch, broken, new Dictionary<string, string>());
            await SetRawRefsAsync(scratch, broken, raw);
            problems.Add(new Seeded(broken, "", "unreadable", raw));
        }

        // Connections the script must not name.
        var clean = new List<string>();
        foreach (var field in new[] { "Password", "Legacy" })
        {
            var id = NewConnectionId();
            var reference = await SeedReferenceAsync(scratch, id, Kind.Owned, field);
            await AddConnectionAsync(scratch, id, new Dictionary<string, string> { [field] = reference });
            clean.Add(id);
        }

        var none = NewConnectionId();
        await AddConnectionAsync(scratch, none, new Dictionary<string, string>());
        clean.Add(none);
        return (problems, clean);
    }

    // ---- A2.6 ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_script_returns_exactly_the_seeded_problem_rows_with_the_right_class_and_nothing_that_identifies_a_secret(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Sec22InvA_", rcsi);
        var (problems, clean) = await SeedEverythingAsync(scratch);

        var (columns, rows) = await RunAsync(scratch);

        columns.Should().Equal(ExpectedColumns);
        rows.Select(r => (r.ConnectionId, FieldKey: r.FieldKey ?? "", r.Class)).Should().BeEquivalentTo(
            problems.Select(p => (p.ConnectionId, p.Field, p.Expected)),
            "exactly the seeded problems, each once, with the class the platform's own rule gives it");
        rows.Select(r => r.ConnectionId).Should().NotIntersectWith(clean, "a field that names its own usable secret is not listed");

        foreach (var row in rows)
        {
            if (row.Class.StartsWith("foreign_", StringComparison.Ordinal))
            {
                row.ForeignProvider.Should().NotBeNullOrEmpty(row.ConnectionId);
                row.ForeignOwnerName.Should().NotBeNullOrEmpty(row.ConnectionId);
            }
            else
            {
                (row.ForeignProvider, row.ForeignOwnerName).Should().Be((null, null), "only a foreign secret is described");
            }
        }

        rows.Where(r => r.State == "Retired").Should().ContainSingle("the retired connection is listed, with its state");
        var otherProvider = rows.Single(r => r.ConnectionId == problems[6].ConnectionId);
        otherProvider.ForeignProvider.Should().Be("platform-setting");
        otherProvider.ForeignOwnerName.Should().Be("platform-setting:notifications.smtp.password");
        rows.Single(r => r.ConnectionId == problems[8].ConnectionId).ForeignOwnerName.Should().StartWith("connection:").And.EndWith($":{Field}");

        // Nothing in any cell identifies a secret: no reference in any spelling, no secret id, no material.
        var everything = string.Join("\n", rows.Select(r => string.Join("|", r.ConnectionId, r.ConnectionName, r.State, r.FieldKey, r.Class, r.ForeignProvider, r.ForeignOwnerName)));
        foreach (var problem in problems.Where(p => Guid.TryParse(p.Reference, out _)))
        {
            everything.Should().NotContainEquivalentOf(problem.Reference);
            everything.Should().NotContainEquivalentOf(problem.Reference.Replace("-", string.Empty));
        }

        everything.Should().NotContain(ForeignMaterial).And.NotContain(OwnedMaterial);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_clean_catalog_and_an_empty_one_return_nothing(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Sec22InvB_", rcsi);
        (await RunAsync(scratch)).Rows.Should().BeEmpty("an empty catalog has nothing to list");

        foreach (var field in new[] { "Password", "ApiKey", "AnythingLegacy" })
        {
            var id = NewConnectionId();
            var reference = await SeedReferenceAsync(scratch, id, Kind.Owned, field);
            await AddConnectionAsync(scratch, id, new Dictionary<string, string> { [field] = reference });
        }

        await AddConnectionAsync(scratch, NewConnectionId(), new Dictionary<string, string>());

        (await RunAsync(scratch)).Rows.Should().BeEmpty("every field names a secret it owns");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_script_runs_as_a_user_that_can_only_read_the_three_tables_changes_nothing_and_is_repeatable(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Sec22InvC_", rcsi);
        var (problems, _) = await SeedEverythingAsync(scratch);
        await scratch.ExecuteAsync(
            $"""
            CREATE USER [{ReaderUser}] WITHOUT LOGIN;
            GRANT SELECT ON dbo.Connections TO [{ReaderUser}];
            GRANT SELECT ON dbo.SecretDescriptors TO [{ReaderUser}];
            GRANT SELECT ON dbo.SecretVersions TO [{ReaderUser}];
            """);
        var asOwner = (await RunAsync(scratch)).Rows;
        var before = await ConnectionSqlHarness.SnapshotAsync(scratch);

        var asReader = await RunAsync(scratch, ReaderUser);
        var again = await RunAsync(scratch, ReaderUser);

        asReader.Rows.Should().BeEquivalentTo(asOwner, "the read-only user sees the same catalog");
        asReader.Rows.Should().HaveCount(problems.Count);
        again.Rows.Should().Equal(asReader.Rows);
        (await ConnectionSqlHarness.SnapshotAsync(scratch)).Should().Be(before, "the script wrote nothing");

        // The controls that make the above meaningful: this user really is limited to reading those three tables.
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

        (await ErrorNumberOf("UPDATE dbo.Connections SET Name = N'changed';")).Should().Be(229, "no permission to write");
        (await ErrorNumberOf("SELECT COUNT(*) FROM dbo.Users;")).Should().Be(229, "no permission to read anything else");
        (await ErrorNumberOf("DELETE FROM dbo.SecretVersions;")).Should().Be(229);
    }

    // ---- what the script is ----

    [Fact]
    public void The_script_has_no_statement_that_writes_creates_or_executes_and_never_reads_the_payload()
    {
        var code = Regex.Replace(Regex.Replace(ScriptText(), @"/\*.*?\*/", " ", RegexOptions.Singleline), @"--[^\r\n]*", " ");

        Regex.IsMatch(code, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|GRANT|REVOKE|DENY|EXEC|EXECUTE|BULK|INTO|OPENROWSET|OPENQUERY|WAITFOR|DBCC|BACKUP|RESTORE|SHUTDOWN)\b", RegexOptions.IgnoreCase)
            .Should().BeFalse("the inventory is read-only");
        Regex.IsMatch(code, @"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline).Should().BeFalse("it runs as one batch");
        code.Should().NotContain("#", "no temporary table");
        code.Should().NotContainEquivalentOf("ProtectedPayload").And.NotContainEquivalentOf("KeyId", "no secret material is ever selected");
        code.Should().Contain("SELECT").And.Contain("OPENJSON");
    }
}
