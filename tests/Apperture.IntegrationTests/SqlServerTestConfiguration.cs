using Apperture.SqlTestTargets;
using Microsoft.Data.SqlClient;

namespace Apperture.IntegrationTests;

/// <summary>
/// Resolves SQL Server connection strings for integration tests without implicitly assuming
/// <c>localhost:1433</c>. Supports Aspire dynamic ports via explicit or injected connection strings.
/// </summary>
/// <remarks>
/// Every candidate is checked by <see cref="SqlTestTargetGuard"/> before anything connects to it, and a refusal is thrown (as
/// <see cref="SqlTestTargetRefusedException"/>) rather than skipped to the next candidate or reported as an unreachable server:
/// the development SQL server must never be reached by falling through from an unreachable test server.
/// </remarks>
public static class SqlServerTestConfiguration
{
    /// <summary>Primary explicit integration-test connection string (any SQL Server; catalog may be overridden).</summary>
    public const string ExplicitConnectionStringVariable = "APPERTURE_TEST_SQL_CONNECTION_STRING";

    /// <summary>Legacy full connection string for Management Host governance harness (CI).</summary>
    public const string GovernanceConnectionStringVariable = "APPERTURE_TEST_SQL";

    /// <summary>Aspire / script-injected default connection (Development orchestration).</summary>
    public const string DefaultConnectionStringVariable = "ConnectionStrings__DefaultConnection";

    private const string LegacyDefaultServer = "localhost,1433";
    private const string DefaultIntegrationDatabase = "Apperture_IntegrationTests";
    private const string DefaultGovernanceDatabase = "Apperture_ManagementGovernanceTests";
    private const string DefaultUser = "sa";

    private static bool _envLoaded;
    private static string? _envFilePathOverrideForTests;

    /// <summary>Human-readable source of the last resolved connection (no secrets).</summary>
    public static string? LastResolutionSource { get; private set; }

    /// <summary>Server endpoint from the last resolved connection (no secrets).</summary>
    public static string? LastResolvedDataSource { get; private set; }

    /// <summary>Database/catalog from the last resolved connection (no secrets).</summary>
    public static string? LastResolvedCatalog { get; private set; }

    /// <summary>Authentication mode from the last resolved connection (no secrets).</summary>
    public static string? LastResolvedAuthMode { get; private set; }

    internal static void SetEnvFilePathOverrideForTests(string? path)
    {
        _envFilePathOverrideForTests = path;
        _envLoaded = false;
    }

    internal static void ResetEnvLoadForTests() => _envLoaded = false;

    /// <summary>
    /// Ensures the test database exists. Call before running migrations when using a fresh SQL Server.
    /// Connects to master and creates the database if missing.
    /// </summary>
    public static void EnsureTestDatabaseExists(string connectionString)
    {
        // Before anything connects: only a disposable catalog on a disposable test server may be created.
        SqlTestTargetGuard.EnsureDisposableTarget(connectionString, "create a test catalog");

        RecordConnectionMetadata(connectionString);
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        using var conn = new SqlConnection(builder.ConnectionString);
        try
        {
            conn.Open();
        }
        catch (SqlException ex)
        {
            // Not an InvalidOperationException: older helpers catch that type to mean "unreachable" (see SqlTestExecution).
            throw SqlTestExecution.Unavailable(DescribeConnectionFailure(), ex);
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{database.Replace("'", "''")}')
            CREATE DATABASE [{database.Replace("]", "]]")}]
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Connection string for general SqlServer integration tests (<see cref="DefaultIntegrationDatabase"/>).
    /// Skips unreachable Aspire ephemeral endpoints and falls through to the next configured source.
    /// </summary>
    public static string? GetConnectionString() =>
        ResolveConnectionString(
            DefaultIntegrationDatabase,
            allowGovernanceOverride: false,
            requireReachable: !_skipReachabilityProbeForTests);

    /// <summary>
    /// Connection string for Management Host governance HTTP tests (<see cref="DefaultGovernanceDatabase"/>).
    /// </summary>
    public static string? GetGovernanceConnectionString() =>
        ResolveConnectionString(
            DefaultGovernanceDatabase,
            allowGovernanceOverride: true,
            requireReachable: !_skipReachabilityProbeForTests);

    /// <summary>
    /// Returns a reachable connection string or throws <see cref="SqlTestUnavailableException"/> with configuration guidance. This is
    /// the way a SQL test obtains its server; <see cref="GetConnectionString"/> returns null instead and is for the helpers and
    /// the configuration tests only (<c>SqlSoftSkipGuardTests</c> refuses it anywhere else).
    /// </summary>
    public static string RequireConnectionString()
    {
        var connectionString = ResolveConnectionString(
            DefaultIntegrationDatabase,
            allowGovernanceOverride: false,
            requireReachable: !_skipReachabilityProbeForTests);
        if (connectionString is null)
            throw SqlTestExecution.Unavailable(DescribeConnectionFailure());

        return connectionString;
    }

    /// <summary>
    /// When exactly <c>1</c> in the process environment, SQL is resolved from the process environment alone: no <c>.env</c>, no
    /// runtime endpoint file. <c>scripts/dev/Test-SqlExecutionHonesty.ps1</c> sets it so that its unreachable target cannot be
    /// replaced by a developer's own configuration. It only removes sources, so it can make a run fail but never pass.
    /// </summary>
    public const string ProcessEnvironmentOnlyVariable = "APPERTURE_TEST_SQL_PROCESS_ENV_ONLY";

    private static bool ProcessEnvironmentOnly =>
        Environment.GetEnvironmentVariable(ProcessEnvironmentOnlyVariable) == "1";

    private static bool _skipReachabilityProbeForTests;
    private static bool _ignoreRuntimeEndpointFilesForTests;

    /// <summary>Test-only: do not read an Aspire endpoint from the checkout's runtime files, so "nothing configured" is reproducible.</summary>
    internal static void SetIgnoreRuntimeEndpointFilesForTests(bool ignore) => _ignoreRuntimeEndpointFilesForTests = ignore;

    /// <summary>Test-only: prefer configured order without probing TCP (preference unit tests).</summary>
    internal static void SetSkipReachabilityProbeForTests(bool skip) => _skipReachabilityProbeForTests = skip;

    /// <summary>Sanitized connection summary for diagnostics (no secrets).</summary>
    public static string DescribeConnectionSummary(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "connection string: (none)";

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var auth = builder.IntegratedSecurity
                ? "integrated security"
                : string.IsNullOrWhiteSpace(builder.UserID)
                    ? "sql auth (no user)"
                    : $"sql auth (user {builder.UserID})";
            var catalog = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "(default)" : builder.InitialCatalog;
            return $"data source={builder.DataSource}; database={catalog}; auth={auth}";
        }
        catch
        {
            return "connection string: (unparseable)";
        }
    }

    /// <summary>Explains how SQL was resolved (or how to configure it). Never includes secrets.</summary>
    public static string DescribeActiveConfiguration()
    {
        if (LastResolutionSource is null)
        {
            return
                "SQL integration tests are not configured. Set one of: " +
                $"{ExplicitConnectionStringVariable}, " +
                $"{DefaultConnectionStringVariable}, " +
                "or MSSQL_SA_PASSWORD (legacy; defaults to localhost,1433). " +
                "For Aspire dev SQL on a dynamic host port, prefer " +
                $"{ExplicitConnectionStringVariable} or {DefaultConnectionStringVariable}.";
        }

        var endpoint = DescribeResolvedEndpoint();

        if (LastResolutionSource.StartsWith("legacy", StringComparison.Ordinal))
        {
            return
                $"SQL integration tests are using legacy MSSQL_SA_PASSWORD resolution ({endpoint}). " +
                "If Aspire SQL runs on a dynamic port, set " +
                $"{ExplicitConnectionStringVariable} or {DefaultConnectionStringVariable} instead of relying on localhost:1433.";
        }

        return
            $"SQL integration tests resolved SQL from {LastResolutionSource} ({endpoint}).";
    }

    /// <summary>Diagnostic when a resolved connection string cannot open SQL (no secrets).</summary>
    public static string DescribeConnectionFailure()
    {
        var endpoint = DescribeResolvedEndpoint();
        return
            $"{DescribeActiveConfiguration()} " +
            $"Connection failed ({endpoint}). " +
            "Verify the endpoint, credentials, and that you are not pointing at a stale Aspire ephemeral SQL port " +
            $"(clear or refresh {ExplicitConnectionStringVariable} / {DefaultConnectionStringVariable} after AppHost restart). " +
            "Do not hard-code Aspire dynamic ports in .env.";
    }

    internal static bool TryParseEnvLine(string line, out string key, out string? value)
    {
        key = string.Empty;
        value = null;

        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#')
            return false;

        var eq = trimmed.IndexOf('=');
        if (eq <= 0)
            return false;

        key = trimmed[..eq].Trim();
        if (key.Length == 0)
            return false;

        value = UnquoteEnvValue(trimmed[(eq + 1)..].TrimStart());
        return true;
    }

    private static string? ResolveConnectionString(
        string targetDatabase,
        bool allowGovernanceOverride,
        bool requireReachable)
    {
        EnsureEnvLoaded();
        LastResolutionSource = null;
        LastResolvedDataSource = null;
        LastResolvedCatalog = null;
        LastResolvedAuthMode = null;

        string? firstConfigured = null;
        string? firstSource = null;

        foreach (var candidate in EnumerateCandidates(targetDatabase, allowGovernanceOverride))
        {
            firstConfigured ??= candidate.ConnectionString;
            firstSource ??= candidate.Source;

            // Refuse before the reachability probe (or anything else) connects. A refusal is thrown, never skipped: falling
            // through from an unreachable test server to a development server is exactly what this prevents.
            SqlTestTargetGuard.EnsureServerAllowed(candidate.ConnectionString, "resolve a test SQL target");

            // Governance harness supplies a full connection string including catalog — do not rewrite.
            var resolved = candidate.Source == GovernanceConnectionStringVariable
                ? candidate.ConnectionString
                : WithDatabase(candidate.ConnectionString, targetDatabase);
            SqlTestTargetGuard.EnsureDisposableTarget(resolved, "resolve a test SQL target", requireTestsSuffix: allowGovernanceOverride);

            if (requireReachable && !TryOpenMaster(candidate.ConnectionString, out _))
                continue;

            LastResolutionSource = candidate.Source;
            RecordConnectionMetadata(resolved);
            return resolved;
        }

        if (firstConfigured is null)
            return null;

        // No reachable candidate: record the first configured endpoint for diagnostics.
        LastResolutionSource = firstSource;
        var fallback = WithDatabase(firstConfigured, targetDatabase);
        RecordConnectionMetadata(fallback);
        return requireReachable ? null : fallback;
    }

    private static IEnumerable<(string Source, string ConnectionString)> EnumerateCandidates(
        string targetDatabase,
        bool allowGovernanceOverride)
    {
        if (allowGovernanceOverride)
        {
            var governance = Environment.GetEnvironmentVariable(GovernanceConnectionStringVariable);
            if (!string.IsNullOrWhiteSpace(governance))
                yield return (GovernanceConnectionStringVariable, governance.Trim());
        }

        var explicitConnection = Environment.GetEnvironmentVariable(ExplicitConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(explicitConnection))
            yield return (ExplicitConnectionStringVariable, explicitConnection.Trim());

        var defaultConnection = Environment.GetEnvironmentVariable(DefaultConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(defaultConnection))
            yield return (DefaultConnectionStringVariable, defaultConnection.Trim());

        // Aspire / Manage.ps1 sometimes publish the live endpoint here after AppHost start.
        var aspireEndpoint = _ignoreRuntimeEndpointFilesForTests || ProcessEnvironmentOnly ? null : TryReadAspireSqlEndpointFromRuntimeFiles();
        if (!string.IsNullOrWhiteSpace(aspireEndpoint))
            yield return ("aspire-runtime-endpoint", aspireEndpoint);

        var password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            yield break;

        var server = Environment.GetEnvironmentVariable("MSSQL_TEST_SERVER") ?? LegacyDefaultServer;
        var database = allowGovernanceOverride
            ? targetDatabase
            : Environment.GetEnvironmentVariable("MSSQL_TEST_DATABASE") ?? targetDatabase;
        var user = Environment.GetEnvironmentVariable("MSSQL_TEST_USER") ?? DefaultUser;

        yield return (
            $"legacy MSSQL_SA_PASSWORD (server {server})",
            $"Server={server};Database={database};User Id={user};Password={password};TrustServerCertificate=True;");
    }

    private static bool TryOpenMaster(string connectionString, out Exception? error)
    {
        error = null;
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master",
                ConnectTimeout = 3
            };
            using var conn = new SqlConnection(builder.ConnectionString);
            conn.Open();
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    /// <summary>
    /// Best-effort discovery of a live Aspire SQL endpoint written by local orchestration.
    /// Never invents a hard-coded ephemeral port.
    /// </summary>
    private static string? TryReadAspireSqlEndpointFromRuntimeFiles()
    {
        try
        {
            var repoRoot = FindRepoRootDirectory();
            if (repoRoot is null)
                return null;

            var candidates = new[]
            {
                Path.Combine(repoRoot, "scripts", ".runtime", "dev", "sql-connection.txt"),
                Path.Combine(repoRoot, "scripts", ".runtime", "dev", "ConnectionStrings__DefaultConnection"),
                Path.Combine(repoRoot, "artifacts", "dev", "sql-connection.txt"),
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                    continue;

                var raw = File.ReadAllText(path).Trim();
                if (raw.Length == 0)
                    continue;

                // Accept either a full connection string or host,port
                if (raw.Contains('=', StringComparison.Ordinal))
                    return raw;

                var password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
                if (string.IsNullOrWhiteSpace(password))
                    continue;

                return $"Server={raw};User Id=sa;Password={password};TrustServerCertificate=True;Initial Catalog=Apperture;";
            }
        }
        catch
        {
            // Discovery is best-effort.
        }

        return null;
    }

    private static string? FindRepoRootDirectory()
    {
        foreach (var startDir in GetSearchRoots())
        {
            var dir = startDir;
            for (var i = 0; i < 12 && dir is not null; i++)
            {
                if (LooksLikeRepoRoot(dir.FullName))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        return null;
    }

    private static string DescribeResolvedEndpoint()
    {
        var dataSource = LastResolvedDataSource ?? "(unknown)";
        var catalog = LastResolvedCatalog ?? "(unknown)";
        var auth = LastResolvedAuthMode ?? "(unknown)";
        return $"data source={dataSource}; database={catalog}; auth={auth}";
    }

    private static string WithDatabase(string connectionString, string database)
    {
        var builder = new SqlConnectionStringBuilder(connectionString.Trim())
        {
            InitialCatalog = database
        };
        return builder.ConnectionString;
    }

    private static void RecordConnectionMetadata(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            LastResolvedDataSource = builder.DataSource;
            LastResolvedCatalog = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "(default)" : builder.InitialCatalog;
            LastResolvedAuthMode = builder.IntegratedSecurity
                ? "integrated security"
                : string.IsNullOrWhiteSpace(builder.UserID)
                    ? "sql auth"
                    : $"sql auth (user {builder.UserID})";
        }
        catch
        {
            LastResolvedDataSource = null;
            LastResolvedCatalog = null;
            LastResolvedAuthMode = null;
        }
    }

    /// <summary>
    /// Loads selected keys from repo-root <c>.env</c> when not already set in the process environment.
    /// Process environment takes precedence over file values.
    /// </summary>
    private static void EnsureEnvLoaded()
    {
        if (_envLoaded)
            return;

        _envLoaded = true;

        if (ProcessEnvironmentOnly)
            return;

        var envPath = _envFilePathOverrideForTests ?? FindRepoRootEnv();
        if (envPath is null || !File.Exists(envPath))
            return;

        foreach (var line in File.ReadLines(envPath))
        {
            if (!TryParseEnvLine(line, out var key, out var value))
                continue;

            if (!IsSupportedEnvKey(key))
                continue;

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                continue;

            Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
        }
    }

    private static string UnquoteEnvValue(string raw)
    {
        if (raw.Length == 0)
            return raw;

        var commentIndex = raw.IndexOf(" #", StringComparison.Ordinal);
        if (commentIndex > 0)
            raw = raw[..commentIndex].TrimEnd();

        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return raw[1..^1];

        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
            return raw[1..^1];

        return raw;
    }

    private static bool IsSupportedEnvKey(string key) =>
        key.Equals(ExplicitConnectionStringVariable, StringComparison.OrdinalIgnoreCase)
        || key.Equals(GovernanceConnectionStringVariable, StringComparison.OrdinalIgnoreCase)
        || key.Equals(DefaultConnectionStringVariable, StringComparison.OrdinalIgnoreCase)
        || key.Equals("MSSQL_SA_PASSWORD", StringComparison.OrdinalIgnoreCase)
        || key.Equals("MSSQL_TEST_SERVER", StringComparison.OrdinalIgnoreCase)
        || key.Equals("MSSQL_TEST_DATABASE", StringComparison.OrdinalIgnoreCase)
        || key.Equals("MSSQL_TEST_USER", StringComparison.OrdinalIgnoreCase);

    private static string? FindRepoRootEnv()
    {
        foreach (var startDir in GetSearchRoots())
        {
            var dir = startDir;
            for (var i = 0; i < 12 && dir is not null; i++)
            {
                var envFile = Path.Combine(dir.FullName, ".env");
                if (File.Exists(envFile) && LooksLikeRepoRoot(dir.FullName))
                    return envFile;

                dir = dir.Parent;
            }
        }

        return null;
    }

    private static IEnumerable<DirectoryInfo> GetSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<DirectoryInfo>();

        foreach (var path in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full))
                    roots.Add(new DirectoryInfo(full));
            }
            catch
            {
                // ignore invalid paths
            }
        }

        return roots;
    }

    private static bool LooksLikeRepoRoot(string directory) =>
        File.Exists(Path.Combine(directory, "AGENTS.md"))
        || File.Exists(Path.Combine(directory, "Apperture.slnx"))
        || File.Exists(Path.Combine(directory, "Directory.Build.props"));
}
