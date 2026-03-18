namespace Apperture.IntegrationTests;

/// <summary>
/// Resolves SQL Server connection string for integration tests (e.g. Docker SQL on port 1433).
/// Reads MSSQL_SA_PASSWORD from environment; if unset, tries to load from repo-root .env so that
/// "dotnet test" can use the same password as docker-compose without manual export.
/// </summary>
public static class SqlServerTestConfiguration
{
    private const string DefaultServer = "localhost,1433";
    private const string DefaultDatabase = "Apperture_IntegrationTests";
    private const string DefaultUser = "sa";

    /// <summary>
    /// Gets a connection string for SQL Server, or null if not configured (tests should skip).
    /// Uses env MSSQL_SA_PASSWORD; if missing, attempts to load from .env file at repo root.
    /// </summary>
    public static string? GetConnectionString()
    {
        EnsureEnvLoaded();
        var password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            return null;

        var server = Environment.GetEnvironmentVariable("MSSQL_TEST_SERVER") ?? DefaultServer;
        var database = Environment.GetEnvironmentVariable("MSSQL_TEST_DATABASE") ?? DefaultDatabase;
        var user = Environment.GetEnvironmentVariable("MSSQL_TEST_USER") ?? DefaultUser;

        return $"Server={server};Database={database};User Id={user};Password={password};TrustServerCertificate=True;";
    }

    /// <summary>
    /// If MSSQL_SA_PASSWORD is not already set, try to load it from a .env file at repo root.
    /// </summary>
    private static void EnsureEnvLoaded()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")))
            return;

        var envPath = FindRepoRootEnv();
        if (envPath is null || !File.Exists(envPath))
            return;

        foreach (var line in File.ReadLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;
            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = trimmed[..eq].TrimEnd();
            var value = trimmed[(eq + 1)..].TrimStart();
            if (key.Equals("MSSQL_SA_PASSWORD", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
                break;
            }
        }
    }

    private static string? FindRepoRootEnv()
    {
        var baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDir))
            return null;
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 6 && dir != null; i++)
        {
            var envFile = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envFile))
                return envFile;
            dir = dir.Parent;
        }
        return null;
    }
}
