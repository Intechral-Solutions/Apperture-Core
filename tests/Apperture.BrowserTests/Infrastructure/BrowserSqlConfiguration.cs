using Apperture.Persistence.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// Resolves the harness's private SQL database on the developer's existing test SQL Server (the same
/// <c>apperture-test-sql</c> instance the integration tests use). The connection string is read from
/// <c>APPERTURE_TEST_SQL_CONNECTION_STRING</c> or the repo-root <c>.env</c>, and is never logged.
/// </summary>
public static class BrowserSqlConfiguration
{
    public const string ConnectionStringVariable = "APPERTURE_TEST_SQL_CONNECTION_STRING";

    /// <summary>A database owned exclusively by this harness. It is dropped and recreated on every run.</summary>
    public const string ScratchDatabaseName = "Apperture_BrowserTests";

    public static string ResolveScratchConnectionString()
    {
        var baseConnection = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(baseConnection))
            baseConnection = ReadFromRepoEnvFile();

        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            throw new InvalidOperationException(
                $"No test SQL connection string. Set {ConnectionStringVariable} or add it to the repo-root .env " +
                "(see tests/README.md, 'Integration test SQL').");
        }

        return new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = ScratchDatabaseName }.ConnectionString;
    }

    /// <summary>Drops (if present) and recreates the harness database so every run starts from an empty schema.</summary>
    public static async Task RecreateScratchDatabaseAsync(string scratchConnectionString)
    {
        var master = new SqlConnectionStringBuilder(scratchConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        try
        {
            await connection.OpenAsync().ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                "Could not reach the test SQL Server. Start the apperture-test-sql container and check " +
                $"{ConnectionStringVariable}. (SQL error {ex.Number}.)", ex);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{ScratchDatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{ScratchDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{ScratchDatabaseName}];
            END;
            CREATE DATABASE [{ScratchDatabaseName}];
            """;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the platform schema before the host starts, exactly as <c>PersistenceMigrationHostedService</c> would.
    /// On a brand-new database the host's own startup writers (permission repair) race each other, which is a
    /// first-run behavior the harness must not depend on.
    /// </summary>
    public static async Task MigrateAsync(string scratchConnectionString)
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>().UseSqlServer(scratchConnectionString).Options;
        await using var db = new AppertureDbContext(options);
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }

    private static string? ReadFromRepoEnvFile()
    {
        var root = FindRepoRoot();
        if (root is null)
            return null;

        var path = Path.Combine(root, ".env");
        if (!File.Exists(path))
            return null;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0 || !line[..eq].Trim().Equals(ConnectionStringVariable, StringComparison.Ordinal))
                continue;

            // Read the raw value: a connection string legitimately contains ';' and must not be shell-parsed.
            return line[(eq + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }

    public static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }
}
